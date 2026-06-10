using MiniController.Web.Systems;

namespace MiniController.Web.Services;

/// <summary>
/// Polls every registered system on its own cadence, always-on, regardless of
/// whether a browser/touchscreen is connected. This is what keeps the dashboard
/// live and drives any future regulation loops.
/// </summary>
public sealed class StatusPollingService(ISystemRegistry registry, ILogger<StatusPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Status polling service started for {Count} system(s).", registry.Modules.Count);

        // Track the next due time per module so each polls at its own interval.
        var nextDue = registry.Modules.ToDictionary(m => m.Id, _ => DateTime.MinValue);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;

            foreach (var module in registry.Modules)
            {
                if (now < nextDue[module.Id])
                    continue;

                try
                {
                    await module.PollAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Poll failed for system {Id}.", module.Id);
                }

                nextDue[module.Id] = DateTime.UtcNow.AddSeconds(module.PollSeconds);
            }

            try
            {
                // Tick at the finest cadence any module needs (min 5s).
                var tick = registry.Modules.Count == 0
                    ? 30
                    : Math.Max(5, registry.Modules.Min(m => m.PollSeconds));
                await Task.Delay(TimeSpan.FromSeconds(tick), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Status polling service stopped.");
    }
}
