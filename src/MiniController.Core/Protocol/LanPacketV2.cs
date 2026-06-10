using System.Buffers.Binary;
using MiniController.Core.Crypto;

namespace MiniController.Core.Protocol;

/// <summary>
/// The V2 LAN packet (0x5A5A …), ported from lan.py _Packet.
/// Wraps an appliance frame: 40-byte header + AES-ECB(frame) + 16-byte MD5 signature.
/// For V3 devices this packet is itself carried inside the encrypted 8370 envelope.
/// </summary>
public static class LanPacketV2
{
    public static byte[] Encode(long deviceId, ReadOnlySpan<byte> commandFrame)
    {
        var encrypted = MideaSecurity.EncryptAes(commandFrame);

        var length = 40 + encrypted.Length + 16;
        var packet = new byte[length];

        packet[0] = 0x5A;
        packet[1] = 0x5A;          // start of packet
        packet[2] = 0x01;
        packet[3] = 0x11;          // message type
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4), (ushort)length);
        packet[6] = 0x20;          // magic bytes
        packet[7] = 0x00;
        // 4 bytes message id (8..11) left zero
        Timestamp().CopyTo(packet.AsSpan(12)); // 8-byte timestamp
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(20), deviceId);
        // 12 unknown bytes (28..39) left zero
        encrypted.CopyTo(packet.AsSpan(40));

        var sign = MideaSecurity.Sign(packet.AsSpan(0, 40 + encrypted.Length));
        sign.CopyTo(packet.AsSpan(40 + encrypted.Length));
        return packet;
    }

    public static byte[] Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
            throw new InvalidOperationException($"Packet too short: {Convert.ToHexString(data)}");
        if (data[0] != 0x5A || data[1] != 0x5A)
            throw new InvalidOperationException($"Unsupported packet: {Convert.ToHexString(data)}");

        int length = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (data.Length < length)
            throw new InvalidOperationException(
                $"Packet truncated. Expected {length}, have {data.Length}.");

        var packet = data[..length];
        var encryptedFrame = packet[40..^16];
        var rxSign = packet[^16..];

        var calcSign = MideaSecurity.Sign(packet[..^16]);
        if (!rxSign.SequenceEqual(calcSign))
            throw new InvalidOperationException("Calculated and received MD5 signature do not match.");

        return MideaSecurity.DecryptAes(encryptedFrame);
    }

    /// <summary>8-byte timestamp: YYYYMMDDHHMMSSmm, each byte a 2-digit component (little-endian order).</summary>
    private static byte[] Timestamp()
    {
        var now = DateTime.UtcNow;
        return
        [
            (byte)(now.Millisecond / 10),
            (byte)now.Second,
            (byte)now.Minute,
            (byte)now.Hour,
            (byte)now.Day,
            (byte)now.Month,
            (byte)(now.Year % 100),
            (byte)(now.Year / 100),
        ];
    }
}
