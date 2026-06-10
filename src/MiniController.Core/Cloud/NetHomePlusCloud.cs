using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MiniController.Core.Crypto;

namespace MiniController.Core.Cloud;

public sealed class CloudException(string message) : Exception(message);

/// <summary>
/// Minimal NetHome Plus cloud client — just enough to retrieve the per-device
/// token/key needed for V3 local control. Ported from cloud.py (NetHomePlusCloud).
/// Run this once; persist the returned token/key and never call the cloud again.
/// </summary>
public sealed class NetHomePlusCloud
{
    private const string AppId = "1017";
    private const string BaseUrl = "https://mapp.appsmb.com";
    private const string AppKey = "3742e9e5842d4ad59c2db887e12449f9";

    private readonly HttpClient _http;
    private readonly string _account;
    private readonly string _password;
    private readonly string _deviceId = RandomHex(8);

    private string _loginId = "";
    private string _sessionId = "";

    public NetHomePlusCloud(string account, string password, HttpClient? http = null)
    {
        _account = account;
        _password = password;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task LoginAsync(CancellationToken ct = default)
    {
        _loginId = await GetLoginIdAsync(ct).ConfigureAwait(false);

        var result = await ApiRequestAsync("/v1/user/login", new Dictionary<string, string>
        {
            ["loginAccount"] = _account,
            ["password"] = EncryptPassword(_loginId, _password),
        }, ct).ConfigureAwait(false);

        _sessionId = result.GetProperty("sessionId").GetString()
            ?? throw new CloudException("No sessionId in login response.");
    }

    /// <summary>Fetch (token, key) for a udpid. Try both endiannesses of the device id at the call site.</summary>
    public async Task<(string Token, string Key)> GetTokenAsync(string udpid, CancellationToken ct = default)
    {
        var result = await ApiRequestAsync("/v1/iot/secure/getToken", new Dictionary<string, string>
        {
            ["udpid"] = udpid,
        }, ct).ConfigureAwait(false);

        foreach (var entry in result.GetProperty("tokenlist").EnumerateArray())
        {
            if (entry.GetProperty("udpId").GetString() == udpid)
                return (entry.GetProperty("token").GetString()!, entry.GetProperty("key").GetString()!);
        }

        throw new CloudException($"No token/key found for udpid {udpid}.");
    }

    /// <summary>Convenience: log in (if needed) and resolve token/key for a device id, trying both endians.</summary>
    public async Task<(string Token, string Key)> GetTokenKeyForDeviceAsync(long deviceId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_sessionId))
            await LoginAsync(ct).ConfigureAwait(false);

        CloudException? last = null;
        foreach (var bigEndian in new[] { false, true })
        {
            var idBytes = ToBytes(deviceId, 6, bigEndian);
            var udpid = Convert.ToHexString(MideaSecurity.Udpid(idBytes)).ToLowerInvariant();
            try
            {
                return await GetTokenAsync(udpid, ct).ConfigureAwait(false);
            }
            catch (CloudException e)
            {
                last = e;
            }
        }

        throw last ?? new CloudException("Could not resolve token/key.");
    }

    private async Task<string> GetLoginIdAsync(CancellationToken ct)
    {
        var result = await ApiRequestAsync("/v1/user/login/id/get", new Dictionary<string, string>
        {
            ["loginAccount"] = _account,
        }, ct).ConfigureAwait(false);

        return result.GetProperty("loginId").GetString()
            ?? throw new CloudException("No loginId in response.");
    }

    private async Task<JsonElement> ApiRequestAsync(
        string endpoint, Dictionary<string, string> data, CancellationToken ct)
    {
        var body = BuildRequestBody(data);
        body["sign"] = Sign(endpoint, body);

        using var content = new FormUrlEncodedContent(body);
        using var response = await _http.PostAsync(BaseUrl + endpoint, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        var errorCode = int.Parse(root.GetProperty("errorCode").GetString()
            ?? root.GetProperty("errorCode").GetRawText());
        if (errorCode != 0)
        {
            var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "unknown";
            throw new CloudException($"Cloud API error {errorCode}: {msg}");
        }

        // Clone so the value survives disposal of the JsonDocument.
        return root.GetProperty("result").Clone();
    }

    private Dictionary<string, string> BuildRequestBody(Dictionary<string, string> data)
    {
        var body = new Dictionary<string, string>
        {
            ["appId"] = AppId,
            ["src"] = AppId,
            ["format"] = "2",
            ["clientType"] = "1",
            ["language"] = "en_US",
            ["deviceId"] = _deviceId,
            ["stamp"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            ["sessionId"] = _sessionId,
        };
        foreach (var (k, v) in data)
            body[k] = v;
        return body;
    }

    private static string Sign(string path, Dictionary<string, string> data)
    {
        var query = string.Join("&", data
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        var msg = path + query + AppKey;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(msg))).ToLowerInvariant();
    }

    private static string EncryptPassword(string loginId, string password)
    {
        var pwHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(password))).ToLowerInvariant();
        var loginHash = loginId + pwHash + AppKey;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(loginHash))).ToLowerInvariant();
    }

    private static byte[] ToBytes(long value, int length, bool bigEndian)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }
        if (bigEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static string RandomHex(int byteCount)
    {
        var bytes = new byte[byteCount];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
