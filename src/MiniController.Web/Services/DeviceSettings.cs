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

    // ---- App-side regulation ----
    // When enabled, the app turns the unit ON/OFF based on indoor temperature vs.
    // AppTargetC with a hysteresis window. When disabled, the unit's own thermostat
    // runs the show as before.
    public bool RegulationEnabled { get; set; }

    /// <summary>Hysteresis window in °C. UI exposes this in the user's display unit.</summary>
    public double RegulationThresholdC { get; set; } = 0.5;

    /// <summary>App-remembered target. Pushed to the unit by the regulation loop if drifted.</summary>
    public double AppTargetC { get; set; } = 22;

    /// <summary>Use the ESPHome transport when a host is set.</summary>
    public bool UsesEspHome => !string.IsNullOrWhiteSpace(EspHomeHost);

    private bool LanComplete =>
        !string.IsNullOrWhiteSpace(Ip)
        && DeviceId != 0
        && !string.IsNullOrWhiteSpace(Token)
        && !string.IsNullOrWhiteSpace(Key);

    public bool IsComplete => UsesEspHome || LanComplete;
}
