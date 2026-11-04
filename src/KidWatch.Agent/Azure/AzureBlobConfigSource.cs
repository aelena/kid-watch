using KidWatch.Agent.Configuration;
using KidWatch.Core.Functional;

namespace KidWatch.Agent.Azure;

/// <summary>
/// Phase 3 config source: downloads <c>config.yaml</c> from this device's own
/// container (<c>&lt;device&gt;/config.yaml</c>). Same <see cref="IConfigSource"/>
/// contract as the local-file source, so the poller is unchanged.
/// </summary>
public sealed class AzureBlobConfigSource(AzureStorage storage, AgentOptions options) : IConfigSource
{
    public async Task<Result<string>> ReadRawAsync(CancellationToken ct)
    {
        try
        {
            var blob = storage.Container(options.Device).GetBlobClient("config.yaml");
            var response = await blob.DownloadContentAsync(ct);
            return Result<string>.Ok(response.Value.Content.ToString());
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"could not download {options.Device}/config.yaml: {ex.Message}");
        }
    }
}
