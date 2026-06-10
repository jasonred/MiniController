using System.Security.Cryptography;
using System.Text;

namespace MiniController.Core.Crypto;

/// <summary>
/// Cryptography primitives for the Midea LAN protocol, ported from msmart (lan.py Security class).
/// - Discovery / V2 payloads: AES-ECB (PKCS7) with a key derived from the fixed sign key.
/// - V3 session payloads:     AES-CBC (zero IV, no padding) with the negotiated local key.
/// - V2 packet signature:     MD5(packet + sign key).
/// </summary>
public static class MideaSecurity
{
    private static readonly byte[] SignKey =
        Encoding.ASCII.GetBytes("xhdiwjnchekd4d512chdjx5d8e4c394D2D7S");

    /// <summary>MD5(SignKey) — the AES-ECB key for discovery + V2 payloads.</summary>
    private static readonly byte[] EncKey = MD5.HashData(SignKey);

    private static readonly byte[] ZeroIv = new byte[16];

    // ---- AES-ECB (discovery / V2 application payload) ----

    public static byte[] EncryptAes(ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = EncKey;
        return aes.EncryptEcb(data, PaddingMode.PKCS7);
    }

    public static byte[] DecryptAes(ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = EncKey;
        return aes.DecryptEcb(data, PaddingMode.PKCS7);
    }

    // ---- AES-CBC (V3 session) — zero IV, caller supplies 16-byte aligned data ----

    public static byte[] EncryptAesCbc(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, ZeroIv, PaddingMode.None);
    }

    public static byte[] DecryptAesCbc(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, ZeroIv, PaddingMode.None);
    }

    // ---- V2 packet signature ----

    public static byte[] Sign(ReadOnlySpan<byte> data)
    {
        var buf = new byte[data.Length + SignKey.Length];
        data.CopyTo(buf);
        SignKey.CopyTo(buf.AsSpan(data.Length));
        return MD5.HashData(buf);
    }

    // ---- udpid used to look up the cloud token/key ----

    public static byte[] Udpid(ReadOnlySpan<byte> deviceId)
    {
        var hash = SHA256.HashData(deviceId);
        var result = new byte[16];
        for (var i = 0; i < 16; i++)
            result[i] = (byte)(hash[i] ^ hash[i + 16]);
        return result;
    }

    public static byte[] StrXor(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Inputs must be equal length for XOR.");
        var result = new byte[a.Length];
        for (var i = 0; i < a.Length; i++)
            result[i] = (byte)(a[i] ^ b[i]);
        return result;
    }

    public static byte[] Sha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);
}
