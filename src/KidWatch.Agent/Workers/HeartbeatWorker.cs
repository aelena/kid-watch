using KidWatch.Agent.Configuration;
using KidWatch.Agent.Counters;
using KidWatch.Agent.Logging;
using KidWatch.Core.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Workers;

/// <summary>
/// Phase 1 proof-of-life: waits briefly for the first config (so early events
/// carry the real device name), does counter housekeeping, then emits a periodic
/// heartbeat. Phase 2 replaces the loop with the real process/time engine
/// (enumerate processes, accumulate minutes, evaluate the budget engine,
/// warn/kill, reset at midnight).
/// </summary>
public sealed class HeartbeatWorker(
    IConfigStore store,
    ICounterStore counters,
    IEventSink events,
    ILogger<HeartbeatWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int RetentionDays = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForFirstConfig(stoppingToken);

        // Housekeeping: drop counter rows older than the retention window. Also
        // ensures the SQLite DB exists on first boot.
        var cutoff = DateOnly.FromDateTime(DateTime.Now).AddDays(-RetentionDays);
        counters.DeleteBefore(cutoff);

        await events.WriteAsync(AgentEvent.Agent(store.Device, "started", DateTimeOffset.Now, store.Current?.Version), stoppingToken);
        logger.LogInformation("agent started as {Device}", store.Device);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await events.WriteAsync(
                AgentEvent.Agent(store.Device, "heartbeat", DateTimeOffset.Now, store.Current?.Version),
                stoppingToken);
        }

        await events.WriteAsync(AgentEvent.Agent(store.Device, "stopped", DateTimeOffset.Now), CancellationToken.None);
        logger.LogInformation("agent stopped");
    }

    private async Task WaitForFirstConfig(CancellationToken ct)
    {
        for (var i = 0; i < 50 && store.Current is null && !ct.IsCancellationRequested; i++)
        {
            try { await Task.Delay(200, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
