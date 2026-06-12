using System.Text.Json;
using MiniController.Core.Ac;
using MiniController.Core.Cloud;
using MiniController.Core.Discovery;

namespace MiniController.Web.Services;

/// <summary>
/// Singleton that owns the connection to the AC unit, the latest status, and all
/// control operations. The UI and the background poller both talk to this.
/// </summary>
public sealed class DeviceManager
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly ILogger<DeviceManager> _logger;
    private readonly object _sync = new();

    private IClimateTransport? _transport;

    public DeviceSettings Settings { get; private set; } = new();
    public AcStatus? Status { get; private set; }
    public string? LastError { get; private set; }
    public DateTime? LastUpdatedUtc { get; private set; }

    /// <summary>Raised whenever status/error changes so the UI can re-render.</summary>
    public event Action? Changed;

    public DeviceManager(IWebHostEnvironment env, ILogger<DeviceManager> logger)
    {
        _logger = logger;
        _settingsPath = Path.Combine(env.ContentRootPath, "device.json");
        LoadSettings();
        RebuildTransport();
    }

    public bool IsConfigured => Settings.IsComplete;

    // ---- configuration ----

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
                Settings = JsonSerializer.Deserialize<DeviceSettings>(File.ReadAllText(_settingsPath)) ?? new();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to load device settings.");
        }
    }

    public async Task SaveSettingsAsync(DeviceSettings settings)
    {
        Settings = settings;
        await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(settings, JsonOpts)).ConfigureAwait(false);
        RebuildTransport();
        NotifyChanged();
    }

    private void RebuildTransport()
    {
        lock (_sync)
        {
            _transport?.Dispose();
            _transport = null;
            Status = null;

            if (!Settings.IsComplete)
                return;

            try
            {
                _transport = Settings.UsesEspHome
                    ? new EspHomeClimateTransport(Settings.EspHomeHost.Trim())
                    : new MideaLanTransport(
                        Settings.Ip, Settings.Port, Settings.DeviceId,
                        Convert.FromHexString(Settings.Token),
                        Convert.FromHexString(Settings.Key));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to build transport from settings.");
                LastError = e.Message;
            }
        }
    }

    // ---- operations ----

    /// <summary>All operations return true on success, false if the command/read failed.</summary>
    public Task<bool> RefreshAsync(CancellationToken ct = default) =>
        RunAsync(t => t.RefreshAsync(ct), "Refresh");

    public Task<bool> SetPowerAsync(bool on) => RunAsync(t => t.SetPowerAsync(on), "Power");

    public async Task<bool> SetTemperatureAsync(double celsius)
    {
        // Persist as the app's regulation target so the loop knows what to enforce.
        Settings.AppTargetC = celsius;
        await PersistSettingsAsync().ConfigureAwait(false);
        return await RunAsync(t => t.SetTemperatureAsync(celsius), "Temperature").ConfigureAwait(false);
    }

    public Task<bool> SetModeAsync(OperationalMode mode) => RunAsync(t => t.SetModeAsync(mode), "Mode");

    public Task<bool> SetFanAsync(int fanSpeed) => RunAsync(t => t.SetFanAsync(fanSpeed), "Fan");

    public Task<bool> SetPresetAsync(Preset preset) => RunAsync(t => t.SetPresetAsync(preset), "Preset");

    public Task<bool> SetSwingAsync(SwingMode swing) => RunAsync(t => t.SetSwingAsync(swing), "Swing");

    public Task<bool> SetBeeperAsync(bool on) => RunAsync(t => t.SetBeeperAsync(on), "Beeper");

    public Task<bool> ToggleDisplayAsync() => RunAsync(t => t.ToggleDisplayAsync(), "Display");

    public Task<bool> SwingStepAsync() => RunAsync(t => t.SwingStepAsync(), "SwingStep");

    /// <summary>
    /// If app-side regulation is enabled, decide whether to flip power and/or
    /// re-push the setpoint, then act. Caller should invoke this after a fresh poll.
    /// Mode-agnostic: the app just decides ON vs OFF based on indoor vs target.
    /// The unit's mode (Cool/Heat/Auto) determines what happens once powered on.
    /// </summary>
    public async Task EvaluateRegulationAsync(CancellationToken ct = default)
    {
        if (!Settings.RegulationEnabled) return;

        IClimateTransport? transport;
        lock (_sync) transport = _transport;
        if (transport is null) return;

        var status = Status;
        if (status is null) return;
        if (status.IndoorTemperature is not double indoor) return;

        var target = Settings.AppTargetC;
        var threshold = Math.Max(0.1, Settings.RegulationThresholdC);
        var delta = Math.Abs(indoor - target);

        // 1) Re-push setpoint if the unit drifted (e.g. someone touched the remote).
        if (status.PowerOn && Math.Abs(status.TargetTemperature - target) > 0.1)
        {
            try { Status = await transport.SetTemperatureAsync(target, ct).ConfigureAwait(false); }
            catch (Exception e) { _logger.LogWarning(e, "Regulation: setpoint sync failed."); }
        }

        // 2) Hysteresis on power (mode-agnostic):
        //    OFF -> ON  when room has drifted past the threshold
        //    ON  -> OFF when room is essentially at target
        bool? want = null;
        if (!status.PowerOn && delta > threshold) want = true;
        else if (status.PowerOn && delta < 0.1) want = false;

        if (want is bool on)
        {
            try
            {
                Status = await transport.SetPowerAsync(on, ct).ConfigureAwait(false);
                LastError = null;
                LastUpdatedUtc = DateTime.UtcNow;
                NotifyChanged();
            }
            catch (Exception e)
            {
                LastError = e.GetBaseException().Message;
                _logger.LogWarning(e, "Regulation: power toggle failed.");
            }
        }
    }

    private async Task PersistSettingsAsync()
    {
        try { await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(Settings, JsonOpts)).ConfigureAwait(false); }
        catch (Exception e) { _logger.LogWarning(e, "Failed to persist settings."); }
    }

    private async Task<bool> RunAsync(Func<IClimateTransport, Task<AcStatus>> action, string label)
    {
        IClimateTransport? transport;
        lock (_sync) transport = _transport;
        if (transport is null)
            return false;

        bool ok;
        try
        {
            Status = await action(transport).ConfigureAwait(false);
            LastError = null;
            LastUpdatedUtc = DateTime.UtcNow;
            ok = true;
        }
        catch (Exception e)
        {
            LastError = e.GetBaseException().Message;
            _logger.LogWarning(e, "{Label} failed.", label);
            ok = false;
        }

        NotifyChanged();
        return ok;
    }

    // ---- setup helpers ----

    public Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(string? target = null, CancellationToken ct = default) =>
        MideaDiscovery.DiscoverAsync(target, ct: ct);

    public async Task<(string Token, string Key)> FetchTokenKeyAsync(
        string account, string password, long deviceId, CancellationToken ct = default)
    {
        var cloud = new NetHomePlusCloud(account, password);
        return await cloud.GetTokenKeyForDeviceAsync(deviceId, ct).ConfigureAwait(false);
    }

    private void NotifyChanged() => Changed?.Invoke();
}
