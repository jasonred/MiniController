using System.Globalization;
using System.Text.Json;
using MiniController.Core.Ac;

namespace MiniController.Web.Services;

/// <summary>
/// Talks to the SLWF-01Pro's ESPHome web server. State is read with one-shot REST
/// GETs (climate entity + the sensors/switch, fetched in parallel). Commands go to
/// POST /climate|/switch|/button endpoints. ESPHome speaks °C and folds power into
/// the mode (OFF is a mode), so this maps that vocabulary onto AcStatus.
/// </summary>
public sealed class EspHomeClimateTransport : IClimateTransport
{
    private readonly HttpClient _http;
    private readonly string _id;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Remembered so "power on" can restore a running mode (ESPHome has no separate power).
    private OperationalMode _lastRunningMode = OperationalMode.Cool;

    public EspHomeClimateTransport(string host, string climateId = "air_conditioner")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{host}"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        _id = climateId;
    }

    public async Task<AcStatus> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadStateAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task<AcStatus> SetPowerAsync(bool on, CancellationToken ct = default) =>
        Command($"/climate/{_id}/set?mode={(on ? ModeToEsp(_lastRunningMode) : "OFF")}", ct);

    public Task<AcStatus> SetModeAsync(OperationalMode mode, CancellationToken ct = default) =>
        Command($"/climate/{_id}/set?mode={ModeToEsp(mode)}", ct);

    public Task<AcStatus> SetTemperatureAsync(double celsius, CancellationToken ct = default) =>
        Command($"/climate/{_id}/set?target_temperature={Math.Clamp(celsius, 16, 30).ToString("0.0", CultureInfo.InvariantCulture)}", ct);

    public Task<AcStatus> SetFanAsync(int fanSpeed, CancellationToken ct = default) =>
        Command($"/climate/{_id}/set?{FanQuery(fanSpeed)}", ct);

    public Task<AcStatus> SetPresetAsync(Preset preset, CancellationToken ct = default) =>
        Command($"/climate/{_id}/set?preset={preset.ToString().ToUpperInvariant()}", ct);

    public Task<AcStatus> SetSwingAsync(SwingMode swing, CancellationToken ct = default) =>
        Command($"/climate/{_id}/set?swing_mode={swing.ToString().ToUpperInvariant()}", ct);

    public Task<AcStatus> SetBeeperAsync(bool on, CancellationToken ct = default) =>
        Command($"/switch/{_id}_beeper/{(on ? "turn_on" : "turn_off")}", ct);

    public Task<AcStatus> ToggleDisplayAsync(CancellationToken ct = default) =>
        Command($"/button/{_id}_display_toggle/press", ct);

    public Task<AcStatus> SwingStepAsync(CancellationToken ct = default) =>
        Command($"/button/{_id}_swing_step/press", ct);

    private async Task<AcStatus> Command(string path, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var resp = await _http.PostAsync(path, null, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await Task.Delay(600, ct).ConfigureAwait(false); // let the unit apply before reading back
            return await ReadStateAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // ---- state read (parallel REST GETs) ----

    private sealed record ClimateState(
        string Mode, string? FanMode, string? Preset, string? SwingMode,
        string? Action, double? Current, double Target);

    private async Task<AcStatus> ReadStateAsync(CancellationToken ct)
    {
        var climateTask = GetClimateAsync(ct);
        var outdoorTask = GetSensorAsync($"{_id}_outdoor_temperature", ct);
        var humidityTask = GetSensorAsync($"{_id}_indoor_humidity", ct);
        var powerTask = GetSensorAsync($"{_id}_power_usage", ct);
        var wifiTask = GetSensorAsync($"{_id}_wi-fi_signal", ct);
        var uptimeTask = GetSensorAsync($"{_id}_uptime_days", ct);
        var beeperTask = GetSwitchAsync($"{_id}_beeper", ct);

        await Task.WhenAll(climateTask, outdoorTask, humidityTask, powerTask, wifiTask, uptimeTask, beeperTask)
            .ConfigureAwait(false);

        var c = climateTask.Result ?? throw new InvalidOperationException("No climate state from ESPHome device.");

        var powerOn = !string.Equals(c.Mode, "OFF", StringComparison.OrdinalIgnoreCase);
        var mode = EspToMode(c.Mode);
        if (powerOn) _lastRunningMode = mode;

        var outdoor = outdoorTask.Result;
        if (outdoor is { } o && (o < -40 || o > 70)) outdoor = null; // bogus "off" reading

        return new AcStatus
        {
            PowerOn = powerOn,
            Mode = mode,
            Action = EspToAction(c.Action),
            TargetTemperature = c.Target,
            FanSpeed = EspFanToSpeed(c.FanMode),
            Preset = EspToPreset(c.Preset),
            Swing = EspToSwing(c.SwingMode),
            Beeper = beeperTask.Result ?? false,
            IndoorTemperature = c.Current,
            OutdoorTemperature = outdoor,
            IndoorHumidity = humidityTask.Result is > 0 ? humidityTask.Result : null,
            PowerUsageW = powerTask.Result,
            WifiSignalDbm = wifiTask.Result,
            UptimeDays = uptimeTask.Result,
            Fahrenheit = false,
        };
    }

    private async Task<ClimateState?> GetClimateAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"/climate/{_id}", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var e = doc.RootElement;
        return new ClimateState(
            Mode: e.TryGetProperty("mode", out var m) ? m.GetString() ?? "OFF" : "OFF",
            FanMode: e.TryGetProperty("fan_mode", out var f) ? f.GetString() : null,
            Preset: e.TryGetProperty("preset", out var pr) ? pr.GetString() : null,
            SwingMode: e.TryGetProperty("swing_mode", out var sw) ? sw.GetString() : null,
            Action: e.TryGetProperty("action", out var a) ? a.GetString() : null,
            Current: ReadDouble(e, "current_temperature"),
            Target: ReadDouble(e, "target_temperature") ?? 22);
    }

    private async Task<double?> GetSensorAsync(string id, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync($"/sensor/{id}", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return ReadDouble(doc.RootElement, "value");
        }
        catch { return null; }
    }

    private async Task<bool?> GetSwitchAsync(string id, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync($"/switch/{id}", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var v = doc.RootElement;
            if (v.TryGetProperty("value", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return b.GetBoolean();
            if (v.TryGetProperty("state", out var s))
                return string.Equals(s.GetString(), "ON", StringComparison.OrdinalIgnoreCase);
            return null;
        }
        catch { return null; }
    }

    private static ClimateAction EspToAction(string? action) => (action ?? "").ToUpperInvariant() switch
    {
        "COOLING" => ClimateAction.Cooling,
        "HEATING" => ClimateAction.Heating,
        "IDLE" => ClimateAction.Idle,
        "DRYING" => ClimateAction.Drying,
        "FAN" => ClimateAction.Fan,
        "OFF" => ClimateAction.Off,
        _ => ClimateAction.Unknown,
    };

    // ---- vocabulary mapping ----

    private static string ModeToEsp(OperationalMode mode) => mode switch
    {
        OperationalMode.Auto => "HEAT_COOL",
        OperationalMode.Cool => "COOL",
        OperationalMode.Heat => "HEAT",
        OperationalMode.Dry => "DRY",
        OperationalMode.FanOnly => "FAN_ONLY",
        _ => "HEAT_COOL",
    };

    private static OperationalMode EspToMode(string esp) => esp.ToUpperInvariant() switch
    {
        "COOL" => OperationalMode.Cool,
        "HEAT" => OperationalMode.Heat,
        "DRY" => OperationalMode.Dry,
        "FAN_ONLY" => OperationalMode.FanOnly,
        "HEAT_COOL" => OperationalMode.Auto,
        _ => OperationalMode.Auto, // OFF or unknown
    };

    private static string FanQuery(int fanSpeed) => fanSpeed switch
    {
        (int)FanSpeed.Silent => "custom_fan_mode=silent",
        (int)FanSpeed.Low => "fan_mode=LOW",
        (int)FanSpeed.Medium => "fan_mode=MEDIUM",
        (int)FanSpeed.High => "fan_mode=HIGH",
        (int)FanSpeed.Turbo => "custom_fan_mode=turbo",
        _ => "fan_mode=AUTO",
    };

    private static int EspFanToSpeed(string? fan) => (fan ?? "AUTO").ToUpperInvariant() switch
    {
        "LOW" => (int)FanSpeed.Low,
        "MEDIUM" => (int)FanSpeed.Medium,
        "HIGH" => (int)FanSpeed.High,
        "SILENT" => (int)FanSpeed.Silent,
        "TURBO" => (int)FanSpeed.Turbo,
        _ => (int)FanSpeed.Auto,
    };

    private static Preset EspToPreset(string? preset) => (preset ?? "NONE").ToUpperInvariant() switch
    {
        "BOOST" => Preset.Boost,
        "ECO" => Preset.Eco,
        "SLEEP" => Preset.Sleep,
        _ => Preset.None,
    };

    private static SwingMode EspToSwing(string? swing) => (swing ?? "OFF").ToUpperInvariant() switch
    {
        "BOTH" => SwingMode.Both,
        "VERTICAL" => SwingMode.Vertical,
        "HORIZONTAL" => SwingMode.Horizontal,
        _ => SwingMode.Off,
    };

    /// <summary>ESPHome serializes numbers sometimes as strings ("27.5") and sometimes as numbers.</summary>
    private static double? ReadDouble(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
