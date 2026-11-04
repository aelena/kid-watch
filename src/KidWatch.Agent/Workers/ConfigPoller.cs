using KidWatch.Agent.Configuration;
using KidWatch.Agent.Logging;
using KidWatch.Core.Configuration;
using KidWatch.Core.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Workers;

/// <summary>
/// Periodically reads the config from the source, parses and validates it, and
/// swaps in any newer version. On any failure it keeps last-known-good and logs a
/// rejection event rather than disrupting enforcement (fail-open on config).
/// </summary>
public sealed class ConfigPoller(
    IConfigSource source,
    IConfigStore store,
    IEventSink events,
    ILogger<ConfigPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);

            var minutes = store.Current?.PollMinutes ?? 15;
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var raw = await source.ReadRawAsync(ct);
        if (raw.IsError)
        {
            await Reject(raw.Error, ct);
            return;
        }

        var parsed = ConfigParser.Parse(raw.Value);
        if (parsed.IsError)
        {
            await Reject(parsed.Error, ct);
            return;
        }

        if (store.TrySetIfNewer(parsed.Value))
        {
            logger.LogInformation("config v{Version} applied for {Device}",
                parsed.Value.Version, parsed.Value.Device);
            await events.WriteAsync(
                AgentEvent.ConfigApplied(parsed.Value.Device, parsed.Value.Version, DateTimeOffset.Now), ct);
        }
    }

    private async Task Reject(string reason, CancellationToken ct)
    {
        logger.LogWarning("config rejected, keeping last-known-good: {Reason}", reason);
        await events.WriteAsync(
            AgentEvent.ConfigRejected(store.Device, reason, DateTimeOffset.Now), ct);
    }
}
