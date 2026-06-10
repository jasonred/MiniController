using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MiniController.Core.Crypto;

namespace MiniController.Core.Discovery;

public sealed record DiscoveredDevice(
    string Ip,
    int Port,
    long DeviceId,
    int DeviceType,
    int Version,
    string Name,
    string SerialNumber);

/// <summary>
/// Broadcast discovery of Midea/MrCool devices on the LAN, ported from discover.py.
/// Sends the fixed discovery datagram to UDP 6445/20086 and decrypts the replies.
/// </summary>
public static class MideaDiscovery
{
    private static readonly byte[] DiscoveryMsg =
    [
        0x5a, 0x5a, 0x01, 0x11, 0x48, 0x00, 0x92, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x7f, 0x75, 0xbd, 0x6b, 0x3e, 0x4f, 0x8b, 0x76,
        0x2e, 0x84, 0x9c, 0x6e, 0x57, 0x8d, 0x65, 0x90,
        0x03, 0x6e, 0x9d, 0x43, 0x42, 0xa5, 0x0f, 0x1f,
        0x56, 0x9e, 0xb8, 0xec, 0x91, 0x8e, 0x92, 0xe5,
    ];

    /// <param name="target">
    /// Optional IP to probe directly (unicast). Use when the unit is on another AP/subnet
    /// where broadcasts don't reach (AP isolation, mesh nodes, separate 2.4GHz range).
    /// When null, falls back to LAN-wide broadcast.
    /// </param>
    public static async Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(
        string? target = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var found = new Dictionary<string, DiscoveredDevice>();

        using var udp = new UdpClient { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var destination = string.IsNullOrWhiteSpace(target)
            ? IPAddress.Parse("255.255.255.255")
            : IPAddress.Parse(target.Trim());

        foreach (var port in new[] { 6445, 20086 })
            for (var i = 0; i < 3; i++)
                await udp.SendAsync(DiscoveryMsg, new IPEndPoint(destination, port), ct).ConfigureAwait(false);

        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

        try
        {
            while (!window.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(window.Token).ConfigureAwait(false);
                var ip = result.RemoteEndPoint.Address.ToString();
                if (found.ContainsKey(ip))
                    continue;

                if (TryParse(ip, result.Buffer, out var device) && device is not null)
                    found[ip] = device;
            }
        }
        catch (OperationCanceledException)
        {
            // discovery window elapsed
        }

        return found.Values.ToList();
    }

    private static bool TryParse(string ip, byte[] data, out DiscoveredDevice? device)
    {
        device = null;
        try
        {
            int version;
            if (data.Length >= 2 && data[0] == 0x5A && data[1] == 0x5A)
                version = 2;
            else if (data.Length >= 2 && data[0] == 0x83 && data[1] == 0x70)
                version = 3;
            else
                return false;

            var span = data.AsSpan();
            if (version == 3)
                span = span[8..^16];

            var encrypted = span[40..^16];
            var deviceId = ReadUInt48LittleEndian(span.Slice(20, 6));

            var decrypted = MideaSecurity.DecryptAes(encrypted);

            // IP is the first 4 bytes, reversed.
            var ipAddr = $"{decrypted[3]}.{decrypted[2]}.{decrypted[1]}.{decrypted[0]}";
            var port = BinaryPrimitives.ReadUInt16LittleEndian(decrypted.AsSpan(4));
            var sn = Encoding.ASCII.GetString(decrypted.AsSpan(8, 32));
            var nameLen = decrypted[40];
            var name = Encoding.ASCII.GetString(decrypted.AsSpan(41, nameLen));

            var deviceType = 0;
            var parts = name.Split('_');
            if (parts.Length > 1)
                deviceType = Convert.ToInt32(parts[1], 16);

            device = new DiscoveredDevice(
                string.IsNullOrEmpty(ipAddr) ? ip : ipAddr,
                port, deviceId, deviceType, version, name, sn);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long ReadUInt48LittleEndian(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        for (var i = bytes.Length - 1; i >= 0; i--)
            value = (value << 8) | bytes[i];
        return value;
    }
}
