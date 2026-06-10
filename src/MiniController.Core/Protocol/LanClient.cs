using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using MiniController.Core.Crypto;

namespace MiniController.Core.Protocol;

public class AuthenticationException(string message) : Exception(message);

public class ProtocolException(string message) : Exception(message);

/// <summary>
/// V3 LAN client for a single Midea/MrCool device. Handles the TCP connection,
/// the 8370 framing, the token/key handshake, and encrypted request/response.
/// Ported from lan.py (_LanProtocolV3 + LAN). Not thread-safe; serialize calls per device.
/// </summary>
public sealed class LanClient(string ip, int port, long deviceId) : IDisposable
{
    private const byte PacketTypeHandshakeRequest = 0x0;
    private const byte PacketTypeHandshakeResponse = 0x1;
    private const byte PacketTypeEncryptedResponse = 0x3;
    private const byte PacketTypeEncryptedRequest = 0x6;
    private const byte PacketTypeError = 0xF;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly List<byte> _buffer = [];

    private byte[]? _localKey;
    private DateTime _localKeyExpiresUtc = DateTime.MinValue;
    private int _packetId;

    private byte[]? _token;
    private byte[]? _key;

    public bool IsAuthenticated =>
        _localKey is not null && DateTime.UtcNow < _localKeyExpiresUtc;

    private bool IsConnected => _tcp is { Connected: true };

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Disconnect();
        _tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await _tcp.ConnectAsync(ip, port, timeout.Token).ConfigureAwait(false);
        _stream = _tcp.GetStream();
        _buffer.Clear();
    }

    /// <summary>Perform the V3 token/key handshake and derive the session local key.</summary>
    public async Task AuthenticateAsync(byte[] token, byte[] key, CancellationToken ct = default)
    {
        if (token.Length == 0 || key.Length == 0)
            throw new AuthenticationException("Token and key must be supplied.");

        if (!IsConnected)
            await ConnectAsync(ct).ConfigureAwait(false);

        _buffer.Clear();

        // Send handshake request carrying the token.
        var request = EncodeHandshakeRequest(token);
        await WriteAsync(request, ct).ConfigureAwait(false);

        var packet = await ReadPacketAsync(ct).ConfigureAwait(false);
        var response = ProcessPacket(packet); // 64-byte key material

        _localKey = DeriveLocalKey(key, response);
        _localKeyExpiresUtc = DateTime.UtcNow.AddHours(12);
        _token = token;
        _key = key;

        // Brief settle before issuing commands (matches reference behaviour).
        await Task.Delay(1000, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Send an appliance command frame and return the decoded response frame(s).
    /// Re-connects / re-authenticates transparently if the session dropped or expired.
    /// </summary>
    public async Task<byte[]> SendAsync(byte[] commandFrame, CancellationToken ct = default)
    {
        if (!IsConnected)
            await ConnectAsync(ct).ConfigureAwait(false);

        if (!IsAuthenticated)
        {
            if (_token is null || _key is null)
                throw new AuthenticationException("Not authenticated and no cached token/key.");
            await AuthenticateAsync(_token, _key, ct).ConfigureAwait(false);
        }

        var v2Packet = LanPacketV2.Encode(deviceId, commandFrame);
        var request = EncodeEncryptedRequest(v2Packet);
        await WriteAsync(request, ct).ConfigureAwait(false);

        var packet = await ReadPacketAsync(ct).ConfigureAwait(false);
        var v2Response = ProcessPacket(packet);
        return LanPacketV2.Decode(v2Response);
    }

    // ---- 8370 framing ----

    private byte[] EncodeHandshakeRequest(byte[] token)
    {
        var header = BuildHeader(token.Length, PacketTypeHandshakeRequest);
        var packet = new byte[header.Length + 2 + token.Length];
        header.CopyTo(packet, 0);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(header.Length), (ushort)(_packetId & 0xFFF));
        token.CopyTo(packet, header.Length + 2);
        AdvancePacketId();
        return packet;
    }

    private byte[] EncodeEncryptedRequest(byte[] data)
    {
        if (_localKey is null)
            throw new ProtocolException("Protocol has not been authenticated.");

        var remainder = (data.Length + 2) % 16;
        var pad = remainder != 0 ? 16 - remainder : 0;
        var length = data.Length + pad + 32;

        var header = BuildHeader(length, (byte)(pad << 4 | PacketTypeEncryptedRequest));

        var payload = new byte[2 + data.Length + pad];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0), (ushort)(_packetId & 0xFFF));
        data.CopyTo(payload, 2);
        if (pad > 0)
            RandomNumberGenerator.Fill(payload.AsSpan(2 + data.Length, pad));

        var signed = new byte[header.Length + payload.Length];
        header.CopyTo(signed, 0);
        payload.CopyTo(signed, header.Length);
        var hash = MideaSecurity.Sha256(signed);

        var encrypted = MideaSecurity.EncryptAesCbc(_localKey, payload);

        var packet = new byte[header.Length + encrypted.Length + hash.Length];
        header.CopyTo(packet, 0);
        encrypted.CopyTo(packet, header.Length);
        hash.CopyTo(packet, header.Length + encrypted.Length);

        AdvancePacketId();
        return packet;
    }

    private static byte[] BuildHeader(int length, byte typeByte)
    {
        var header = new byte[6];
        header[0] = 0x83;
        header[1] = 0x70;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)length);
        header[4] = 0x20;
        header[5] = typeByte;
        return header;
    }

    private byte[] ProcessPacket(byte[] packet)
    {
        if (packet[0] != 0x83 || packet[1] != 0x70)
            throw new ProtocolException($"Invalid start of packet: {Convert.ToHexString(packet.AsSpan(0, 2))}");
        if (packet[4] != 0x20)
            throw new ProtocolException($"Invalid magic byte: 0x{packet[4]:X2}");

        var type = packet[5] & 0xF;
        return type switch
        {
            PacketTypeEncryptedResponse => DecodeEncryptedResponse(packet),
            PacketTypeHandshakeResponse => packet[8..], // strip 6-byte header + 2-byte packet id
            PacketTypeError => throw new ProtocolException("Error packet received."),
            _ => throw new ProtocolException($"Unexpected packet type: {type}"),
        };
    }

    private byte[] DecodeEncryptedResponse(byte[] packet)
    {
        if (_localKey is null)
            throw new ProtocolException("Received encrypted data before authentication.");

        var header = packet.AsSpan(0, 6);
        var payload = packet.AsSpan(6, packet.Length - 6 - 32);
        var rxHash = packet.AsSpan(packet.Length - 32, 32);

        var decrypted = MideaSecurity.DecryptAesCbc(_localKey, payload);

        var check = new byte[6 + decrypted.Length];
        header.CopyTo(check);
        decrypted.CopyTo(check.AsSpan(6));
        if (!MideaSecurity.Sha256(check).AsSpan().SequenceEqual(rxHash))
            throw new ProtocolException("Calculated and received SHA256 digest do not match.");

        var pad = header[5] >> 4;
        // strip 2-byte packet id prefix and pad suffix
        return decrypted[2..(decrypted.Length - pad)];
    }

    private static byte[] DeriveLocalKey(byte[] key, byte[] data)
    {
        if (data.Length != 64)
            throw new AuthenticationException("Invalid data length for key handshake.");

        var payload = data.AsSpan(0, 32);
        var rxHash = data.AsSpan(32, 32);

        var decrypted = MideaSecurity.DecryptAesCbc(key, payload);
        if (!MideaSecurity.Sha256(decrypted).AsSpan().SequenceEqual(rxHash))
            throw new AuthenticationException("Calculated and received SHA256 digest do not match.");

        return MideaSecurity.StrXor(decrypted, key);
    }

    private void AdvancePacketId() => _packetId = (_packetId + 1) & 0xFFF;

    // ---- transport ----

    private async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        if (_stream is null)
            throw new ProtocolException("Not connected.");
        await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Read one complete 8370 packet from the stream, buffering across reads.</summary>
    private async Task<byte[]> ReadPacketAsync(CancellationToken ct)
    {
        if (_stream is null)
            throw new ProtocolException("Not connected.");

        var chunk = new byte[4096];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        while (true)
        {
            if (TryExtractPacket(out var packet))
                return packet;

            var read = await _stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false);
            if (read == 0)
                throw new ProtocolException("Connection closed by peer.");
            _buffer.AddRange(chunk.AsSpan(0, read).ToArray());
        }
    }

    private bool TryExtractPacket(out byte[] packet)
    {
        packet = [];

        // Find start of packet (0x8370).
        var start = -1;
        for (var i = 0; i + 1 < _buffer.Count; i++)
        {
            if (_buffer[i] == 0x83 && _buffer[i + 1] == 0x70)
            {
                start = i;
                break;
            }
        }

        if (start == -1)
            return false;
        if (start > 0)
            _buffer.RemoveRange(0, start); // discard leading garbage
        if (_buffer.Count < 6)
            return false;

        var totalSize = ((_buffer[2] << 8) | _buffer[3]) + 8;
        if (_buffer.Count < totalSize)
            return false;

        packet = _buffer.GetRange(0, totalSize).ToArray();
        _buffer.RemoveRange(0, totalSize);
        return true;
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }

    public void Dispose() => Disconnect();
}
