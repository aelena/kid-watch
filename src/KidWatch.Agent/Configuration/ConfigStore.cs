using KidWatch.Core.Configuration;

namespace KidWatch.Agent.Configuration;

/// <summary>Holds the current, atomically-swapped <see cref="AgentConfig"/>.</summary>
public interface IConfigStore
{
    AgentConfig? Current { get; }

    /// <summary>
    /// Replaces the current config when the incoming one is the first, or has a
    /// strictly newer version. Returns true when it was applied.
    /// </summary>
    bool TrySetIfNewer(AgentConfig config);

    /// <summary>Device name: the live config's, else the bootstrap fallback.</summary>
    string Device { get; }
}

public sealed class ConfigStore(AgentOptions options) : IConfigStore
{
    private readonly Lock _gate = new();
    private AgentConfig? _current;

    public AgentConfig? Current
    {
        get { lock (_gate) return _current; }
    }

    public string Device
    {
        get { lock (_gate) return _current?.Device ?? options.Device; }
    }

    public bool TrySetIfNewer(AgentConfig config)
    {
        lock (_gate)
        {
            if (_current is not null && config.Version <= _current.Version)
                return false;
            _current = config;
            return true;
        }
    }
}
