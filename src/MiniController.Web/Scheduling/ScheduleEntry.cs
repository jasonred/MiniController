using MiniController.Core.Ac;
using MiniController.Web.Services;

namespace MiniController.Web.Scheduling;

/// <summary>What a schedule entry does to the unit when it fires.</summary>
public enum SchedulePower { TurnOn, TurnOff }

/// <summary>
/// One time-of-day climate action. Days empty = every day. Mode/Temp/Fan are
/// optional; null means "leave as-is" (except a TurnOff, which just powers down).
/// </summary>
public sealed class ScheduleEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public bool Enabled { get; set; } = true;
    public string? Label { get; set; }

    /// <summary>24-hour "HH:mm" (matches an &lt;input type="time"&gt;).</summary>
    public string Time { get; set; } = "07:00";

    /// <summary>Days this runs on. Empty list = every day.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    public SchedulePower Power { get; set; } = SchedulePower.TurnOn;
    public OperationalMode? Mode { get; set; }
    public double? TargetTemperatureC { get; set; }
    public int? Fan { get; set; }

    public string DaysSummary()
    {
        if (Days.Count == 0) return "Every day";
        var order = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
        return string.Join(" ", order.Where(Days.Contains).Select(d => d.ToString()[..3]));
    }

    public string ActionSummary(TemperatureUnit unit)
    {
        if (Power == SchedulePower.TurnOff) return "Turn off";
        var parts = new List<string> { "On" };
        if (Mode is { } m) parts.Add(m.ToString());
        if (TargetTemperatureC is { } t)
            parts.Add(unit == TemperatureUnit.Fahrenheit ? $"{AppPreferences.CtoF(t):0}°F" : $"{t:0.#}°C");
        if (Fan is { } f) parts.Add($"fan {FanName(f)}");
        return string.Join(" · ", parts);
    }

    private static string FanName(int f) =>
        Enum.IsDefined(typeof(FanSpeed), f) ? ((FanSpeed)f).ToString() : f.ToString();
}
