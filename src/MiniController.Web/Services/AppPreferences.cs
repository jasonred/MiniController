using System.Globalization;
using System.Text.Json;

namespace MiniController.Web.Services;

public enum TemperatureUnit { Celsius, Fahrenheit }

/// <summary>
/// App-wide display preferences (persisted to prefs.json). Currently just the
/// temperature unit. Temperatures are stored/commanded in °C everywhere; this
/// only controls how they're shown and how the setpoint steps.
/// </summary>
public sealed class AppPreferences
{
    private sealed record Prefs(TemperatureUnit Unit);

    private readonly string _path;
    private readonly ILogger<AppPreferences> _logger;

    public TemperatureUnit Unit { get; private set; } = TemperatureUnit.Fahrenheit;

    public event Action? Changed;

    public AppPreferences(IWebHostEnvironment env, ILogger<AppPreferences> logger)
    {
        _logger = logger;
        _path = Path.Combine(env.ContentRootPath, "prefs.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var p = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(_path));
                if (p is not null) Unit = p.Unit;
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to load preferences.");
        }
    }

    public async Task SetUnitAsync(TemperatureUnit unit)
    {
        Unit = unit;
        try { await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(new Prefs(unit))); }
        catch (Exception e) { _logger.LogError(e, "Failed to save preferences."); }
        Changed?.Invoke();
    }

    public string Symbol => Unit == TemperatureUnit.Fahrenheit ? "°F" : "°C";

    /// <summary>Format a °C value in the preferred unit.</summary>
    public string Format(double celsius) =>
        Unit == TemperatureUnit.Fahrenheit
            ? $"{CtoF(celsius):0}°F"
            : $"{celsius.ToString("0.#", CultureInfo.InvariantCulture)}°C";

    public string FormatOrDash(double? celsius) => celsius is null ? "—" : Format(celsius.Value);

    public static double CtoF(double c) => c * 9.0 / 5.0 + 32.0;
    public static double FtoC(double f) => (f - 32.0) * 5.0 / 9.0;
}
