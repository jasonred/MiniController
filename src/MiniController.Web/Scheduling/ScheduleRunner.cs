using MiniController.Web.Services;

namespace MiniController.Web.Scheduling;

/// <summary>
/// Applies schedule entries at their time of day. Ticks every 30s, fires any
/// entry whose time matches the current local minute (deduped so it runs once),
/// and applies the action through DeviceManager. Always-on — this is what makes
/// the schedule work on the Pi with nothing connected.
/// </summary>
public sealed class ScheduleRunner(
    ScheduleStore store, DeviceManager manager, AppPreferences prefs, ILogger<ScheduleRunner> logger)
    : BackgroundService
{
    // entry id -> "yyyyMMddHHmm" it last fired, to avoid double-firing within a minute.
    private readonly Dictionary<string, string> _lastFired = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Schedule runner started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await EvaluateAsync(DateTime.Now, stoppingToken).ConfigureAwait(false); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Schedule evaluation failed.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("Schedule runner stopped.");
    }

    private async Task EvaluateAsync(DateTime now, CancellationToken ct)
    {
        var stamp = now.ToString("yyyyMMddHHmm");

        foreach (var entry in store.Entries)
        {
            if (!entry.Enabled)
                continue;
            if (entry.Days.Count > 0 && !entry.Days.Contains(now.DayOfWeek))
                continue;
            if (!TimeOnly.TryParse(entry.Time, out var t))
                continue;
            if (t.Hour != now.Hour || t.Minute != now.Minute)
                continue;
            if (_lastFired.TryGetValue(entry.Id, out var last) && last == stamp)
                continue;

            _lastFired[entry.Id] = stamp;
            logger.LogInformation("Firing schedule {Id} ({Action}) at {Time}.", entry.Id, entry.ActionSummary(prefs.Unit), entry.Time);
            await ApplyAsync(entry, ct).ConfigureAwait(false);
        }
    }

    private async Task ApplyAsync(ScheduleEntry entry, CancellationToken ct)
    {
        if (entry.Power == SchedulePower.TurnOff)
        {
            await manager.SetPowerAsync(false).ConfigureAwait(false);
            return;
        }

        // Setting a mode also powers the unit on.
        if (entry.Mode is { } mode)
            await manager.SetModeAsync(mode).ConfigureAwait(false);
        else
            await manager.SetPowerAsync(true).ConfigureAwait(false);

        if (entry.TargetTemperatureC is { } temp)
            await manager.SetTemperatureAsync(temp).ConfigureAwait(false);

        if (entry.Fan is { } fan)
            await manager.SetFanAsync(fan).ConfigureAwait(false);
    }
}
