namespace KidWatch.Agent;

/// <summary>
/// File-system and bootstrap options, bound from the "Agent" configuration
/// section. These are the few things the agent needs before it has downloaded a
/// config (paths, and a device-name fallback for early log lines).
/// </summary>
public sealed class AgentOptions
{
    /// <summary>Root for config, logs and the counter DB. Service runs as LocalSystem.</summary>
    public string DataDir { get; set; } = @"C:\ProgramData\pc-kid-watch";

    /// <summary>Phase 1: read config from this local file. Defaults to DataDir\config.yaml.</summary>
    public string? ConfigFile { get; set; }

    /// <summary>Device name used in logs until a config is loaded.</summary>
    public string Device { get; set; } = "unknown-device";

    public string ResolveConfigFile() =>
        string.IsNullOrWhiteSpace(ConfigFile) ? Path.Combine(DataDir, "config.yaml") : ConfigFile!;

    public string LogDir => Path.Combine(DataDir, "logs");

    public string DbFile => Path.Combine(DataDir, "counters.db");
}
