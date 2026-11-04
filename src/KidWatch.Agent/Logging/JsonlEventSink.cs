using KidWatch.Agent.Configuration;
using KidWatch.Core.Events;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Logging;

/// <summary>Writes structured events somewhere durable.</summary>
public interface IEventSink
{
    Task WriteAsync(AgentEvent e, CancellationToken ct = default);
}

/// <summary>
/// Appends events as JSON lines to
/// <c>{LogDir}/{device}/{yyyy-MM-dd}/events.jsonl</c> (local time), mirroring the
/// Azure append-blob layout from specs.md. Phase 3 adds the uploader that ships
/// these same lines to Azure Storage.
/// </summary>
public sealed class JsonlEventSink(
    AgentOptions options,
    IConfigStore config,
    ILogger<JsonlEventSink> logger) : IEventSink
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task WriteAsync(AgentEvent e, CancellationToken ct = default)
    {
        var line = e.ToJsonLine();
        var day = DateTime.Now.ToString("yyyy-MM-dd");
        // Type-first layout mirrors the Azure blob prefixes: <device>/events/<date>.jsonl
        var dir = Path.Combine(options.LogDir, config.Device, "events");
        var file = Path.Combine(dir, day + ".jsonl");

        await _writeLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(dir);
            await File.AppendAllTextAsync(file, line + Environment.NewLine, ct);
        }
        catch (Exception ex)
        {
            // Logging must never crash the agent; surface and move on.
            logger.LogError(ex, "failed to append event to {File}", file);
        }
        finally
        {
            _writeLock.Release();
        }

        logger.LogDebug("event {Line}", line);
    }
}
