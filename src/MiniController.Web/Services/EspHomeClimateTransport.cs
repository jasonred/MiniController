using System.Globalization;
using System.Text.Json;
using MiniController.Core.Ac;

namespace MiniController.Web.Services;

/// <summary>
/// Talks to the SLWF-01Pro's ESPHome web server. State is read from the SSE
/// /events stream (an initial burst dumps every entity); commands go to
/// POST /climate/{id}/set. ESPHome speaks °C and folds power into the mode
/// (OFF is a mode), so this maps that vocabulary onto AcStatus.
/// </summary>
public sealed class EspHomeClimateTransport : IClimateTransport
{
    private readonly HttpClient _http;
    private readonly string _climateId;
    private readonly string _outdoorSensorId;
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
        _climateId = climateId;
        _outdoorSensorId = $"{climateId}_outdoor_temperature";
    }

    public async Task<AcStatus> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await ReadStateAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public Task<AcStatus> SetPowerAsync(bool on, CancellationToken ct = default) =>
        Command(on ? $"mode={ModeToEsp(_lastRunningMode)}" : "mode=OFF", ct);

    public Task<AcStatus> SetModeAsync(OperationalMode mode, CancellationToken ct = default) =>
        Command($"mode={ModeToEsp(mode)}", ct);

    public Task<AcStatus> SetTemperatureAsync(double celsius, CancellationToken ct = default) =>
        Command($"target_temperature={Math.Clamp(celsius, 16, 30).ToString("0.0", CultureInfo.InvariantCulture)}", ct);

    public Task<AcStatus> SetFanAsync(int fanSpeed, CancellationToken ct = default) =>
        Command(FanQuery(fanSpeed), ct);

    private async Task<AcStatus> Command(string query, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var resp = await _http.PostAsync($"/climate/{_climateId}/set?{query}", null, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            // Give the unit a beat to apply before reading back.
            await Task.Delay(600, ct).ConfigureAwait(false);
            return await ReadStateAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Read the initial state burst from /events and build an AcStatus.</summary>
    private async Task<AcStatus> ReadStateAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));

        JsonElement? climate = null;
        double? outdoor = null;

        try
        {
            using var stream = await _http.GetStreamAsync("/events", cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false)) is not null)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var json = line[5..].Trim();
                if (json.Length == 0 || json[0] != '{')
                    continue;

                JsonElement el;
                try { el = JsonDocument.Parse(json).RootElement; }
                catch { continue; }

                if (!el.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();

                if (id == $"climate-{_climateId}")
                    climate = el.Clone();
                else if (id == $"sensor-{_outdoorSensorId}")
                    outdoor = ReadDouble(el, "value");

                if (climate is not null && outdoor is not null)
                    break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 4s window elapsed — use whatever we captured.
        }

        if (climate is null)
            throw new InvalidOperationException("No climate state received from ESPHome device.");

        return BuildStatus(climate.Value, outdoor);
    }

    private AcStatus BuildStatus(JsonElement c, double? outdoor)
    {
        var espMode = c.TryGetProperty("mode", out var m) ? m.GetString() ?? "OFF" : "OFF";
        var powerOn = !string.Equals(espMode, "OFF", StringComparison.OrdinalIgnoreCase);
        var mode = EspToMode(espMode);
        if (powerOn) _lastRunningMode = mode;

        var fan = c.TryGetProperty("fan_mode", out var f) ? f.GetString() : null;

        // Outdoor sensor reports a bogus high value while the unit is off; hide it.
        if (outdoor is { } o && (o < -40 || o > 70)) outdoor = null;

        return new AcStatus
        {
            PowerOn = powerOn,
            Mode = mode,
            TargetTemperature = ReadDouble(c, "target_temperature") ?? 22,
            FanSpeed = EspFanToSpeed(fan),
            IndoorTemperature = ReadDouble(c, "current_temperature"),
            OutdoorTemperature = outdoor,
            Fahrenheit = false,
        };
    }

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
        (int)FanSpeed.Full => "fan_mode=HIGH",
        _ => "fan_mode=AUTO",
    };

    private static int EspFanToSpeed(string? fan) => (fan ?? "AUTO").ToUpperInvariant() switch
    {
        "LOW" => (int)FanSpeed.Low,
        "MEDIUM" => (int)FanSpeed.Medium,
        "HIGH" => (int)FanSpeed.High,
        "SILENT" => (int)FanSpeed.Silent,
        "TURBO" => (int)FanSpeed.Full,
        _ => (int)FanSpeed.Auto,
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
