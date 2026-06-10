namespace MiniController.Web.Services;

/// <summary>Persisted connection details for a single AC unit (stored in device.json).</summary>
public sealed class DeviceSettings
{
    // Preferred path: the SLWF-01Pro ESPHome dongle (just needs its IP/host).
    public string EspHomeHost { get; set; } = "";

    // Legacy path: stock Midea LAN dongle (token/key).
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 6444;
    public long DeviceId { get; set; }
    public string Token { get; set; } = "";  // hex
    public string Key { get; set; } = "";    // hex

    public string Name { get; set; } = "";
    public int PollSeconds { get; set; } = 30;

    /// <summary>Use the ESPHome transport when a host is set.</summary>
    public bool UsesEspHome => !string.IsNullOrWhiteSpace(EspHomeHost);

    private bool LanComplete =>
        !string.IsNullOrWhiteSpace(Ip)
        && DeviceId != 0
        && !string.IsNullOrWhiteSpace(Token)
        && !string.IsNullOrWhiteSpace(Key);

    public bool IsComplete => UsesEspHome || LanComplete;
}
