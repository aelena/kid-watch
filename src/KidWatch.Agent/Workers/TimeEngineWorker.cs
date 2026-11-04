using KidWatch.Agent.Enforcement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Workers;

/// <summary>
/// Drives <see cref="TimeEngine.TickAsync"/> on a fixed cadence. A tick that
/// throws is logged and the loop continues — enforcement must survive a transient
/// failure (e.g. a process that vanished mid-kill).
/// </summary>
public sealed class TimeEngineWorker(TimeEngine engine, ILogger<TimeEngineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("time engine started, tick every {Seconds}s", engine.Interval.TotalSeconds);
        using var timer = new PeriodicTimer(engine.Interval);

        do
        {
            try
            {
                await engine.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "time engine tick failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));

        logger.LogInformation("time engine stopped");
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
