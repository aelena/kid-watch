using Azure.Storage.Blobs.Specialized;
using KidWatch.Agent.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Azure;

/// <summary>
/// Ships today's local event lines to the Azure append blob
/// <c>&lt;device&gt;/events/&lt;date&gt;.jsonl</c> as deltas: each cycle it appends
/// only the bytes written since last time, tracked in a sidecar <c>.offset</c>
/// file. The local JSONL stays the durable queue; a failed upload just retries
/// next cycle.
/// </summary>
public sealed class AzureAppendUploader(
    AzureStorage storage,
    IConfigStore config,
    AgentOptions options,
    ILogger<AzureAppendUploader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("azure uploader started for device {Device}", options.Device);

        // Warm-up: let the first config download and the first events land before
        // the first flush (and so the first upload happens seconds after boot, not
        // a full cadence later).
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "event upload failed; will retry");
            }

            var minutes = config.Current?.UploadMinutes ?? 5;
            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        try { await FlushAsync(CancellationToken.None); } catch { /* best effort on shutdown */ }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var device = options.Device;
        var day = DateTime.Now.ToString("yyyy-MM-dd");
        var localFile = Path.Combine(options.LogDir, device, "events", day + ".jsonl");
        if (!File.Exists(localFile))
            return;

        var offsetFile = localFile + ".offset";
        long uploaded = 0;
        if (File.Exists(offsetFile) && long.TryParse((await File.ReadAllTextAsync(offsetFile, ct)).Trim(), out var o))
            uploaded = o;

        await using var fs = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var size = fs.Length;
        if (uploaded > size) uploaded = 0;   // file was rotated/truncated; resend from start
        if (size <= uploaded) return;         // nothing new

        var blobName = $"events/{day}.jsonl";
        var append = storage.Container(device).GetAppendBlobClient(blobName);
        await append.CreateIfNotExistsAsync(cancellationToken: ct);

        var length = (int)(size - uploaded);
        var buffer = new byte[length];
        fs.Seek(uploaded, SeekOrigin.Begin);
        await fs.ReadExactlyAsync(buffer, 0, length, ct);

        using var delta = new MemoryStream(buffer);
        await append.AppendBlockAsync(delta, cancellationToken: ct);
        await File.WriteAllTextAsync(offsetFile, size.ToString(), ct);

        logger.LogInformation("uploaded {Bytes} new bytes to {Blob}", length, blobName);
    }
}
