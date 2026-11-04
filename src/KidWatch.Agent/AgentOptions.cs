namespace KidWatch.Agent;

/// <summary>
/// File-system and bootstrap options, bound from the "Agent" configuration
/// section. These are the few things the agent needs before it has downloaded a
/// config (paths, its device identity, and where its storage credential lives).
/// </summary>
public sealed class AgentOptions
{
    /// <summary>Root for config, logs and the counter DB. Service runs as LocalSystem.</summary>
    public string DataDir { get; set; } = @"C:\ProgramData\pc-kid-watch";

    /// <summary>
    /// This laptop's device identity. Must equal its Azure container name. Used to
    /// pick the container and group local logs, even before config is downloaded.
    /// </summary>
    public string Device { get; set; } = "unknown-device";

    /// <summary>When true, pull config from Azure and upload events; else local-file only.</summary>
    public bool UseAzure { get; set; }

    /// <summary>Phase 1/dev: read config from this local file instead of Azure.</summary>
    public string? ConfigFile { get; set; }

    /// <summary>
    /// Path to the storage credential (a connection string or account-SAS). In
    /// production this file is DPAPI-encrypted under the machine account; v0 also
    /// accepts plaintext. Defaults to DataDir\secret.dat.
    /// </summary>
    public string? SecretFile { get; set; }

    public string ResolveConfigFile() =>
        string.IsNullOrWhiteSpace(ConfigFile) ? Path.Combine(DataDir, "config.yaml") : ConfigFile!;

    public string ResolveSecretFile() =>
        string.IsNullOrWhiteSpace(SecretFile) ? Path.Combine(DataDir, "secret.dat") : SecretFile!;

    public string LogDir => Path.Combine(DataDir, "logs");

    public string DbFile => Path.Combine(DataDir, "counters.db");
}
