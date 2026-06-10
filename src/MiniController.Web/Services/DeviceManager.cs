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

    public Task<bool> SetTemperatureAsync(double celsius) =>
        RunAsync(t => t.SetTemperatureAsync(celsius), "Temperature");

    public Task<bool> SetModeAsync(OperationalMode mode) => RunAsync(t => t.SetModeAsync(mode), "Mode");

    public Task<bool> SetFanAsync(int fanSpeed) => RunAsync(t => t.SetFanAsync(fanSpeed), "Fan");

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
