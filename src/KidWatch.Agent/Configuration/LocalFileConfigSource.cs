using KidWatch.Core.Functional;

namespace KidWatch.Agent.Configuration;

/// <summary>Where the raw config YAML comes from.</summary>
public interface IConfigSource
{
    Task<Result<string>> ReadRawAsync(CancellationToken ct);
}

/// <summary>
/// Phase 1 source: reads config from a local file. Phase 3 adds an Azure Storage
/// source behind the same interface (download blob to this same string).
/// </summary>
public sealed class LocalFileConfigSource(AgentOptions options) : IConfigSource
{
    public async Task<Result<string>> ReadRawAsync(CancellationToken ct)
    {
        var path = options.ResolveConfigFile();
        if (!File.Exists(path))
            return Result<string>.Fail($"config file not found: {path}");

        try
        {
            var text = await File.ReadAllTextAsync(path, ct);
            return Result<string>.Ok(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"could not read {path}: {ex.Message}");
        }
    }
}
