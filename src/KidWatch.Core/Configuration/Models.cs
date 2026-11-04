namespace KidWatch.Core.Configuration;

/// <summary>What to do when an app rule's budget or schedule is exceeded.</summary>
public enum EnforcementAction { Warn, Kill }

/// <summary>How screenshots are timed (when enabled).</summary>
public enum ScreenshotMode { Fixed, Random }

/// <summary>A half-open time window [Start, End) within a single day.</summary>
public sealed record TimeWindow(TimeOnly Start, TimeOnly End)
{
    public bool Contains(TimeOnly t) => t >= Start && t < End;
}

/// <summary>
/// Allowed windows per day of week. A day absent from the map (or present with
/// no windows) is treated as fully closed.
/// </summary>
public sealed record Schedule(IReadOnlyDictionary<DayOfWeek, IReadOnlyList<TimeWindow>> Windows)
{
    public static readonly Schedule Empty =
        new(new Dictionary<DayOfWeek, IReadOnlyList<TimeWindow>>());

    /// <summary>True if <paramref name="localNow"/> falls inside an allowed window.</summary>
    public bool IsOpen(DateTime localNow) =>
        Windows.TryGetValue(localNow.DayOfWeek, out var windows)
        && windows.Any(w => w.Contains(TimeOnly.FromDateTime(localNow)));
}

/// <summary>A monitored executable family with a daily budget and optional schedule.</summary>
public sealed record AppRule(
    string Name,
    IReadOnlyList<string> Match,
    int DailyLimitMinutes,
    int WarnMinutes,
    EnforcementAction Action,
    Schedule? Schedule);

/// <summary>One scheduled domain: allowed only inside <see cref="Allow"/>.</summary>
public sealed record UrlScheduleRule(string Domain, Schedule Allow);

/// <summary>Domain-level rules: always-denied patterns plus per-domain schedules.</summary>
public sealed record UrlRules(
    IReadOnlyList<string> Deny,
    IReadOnlyList<UrlScheduleRule> Schedule);

/// <summary>Screenshot capture config. Built in v0 but shipped disabled.</summary>
public sealed record ScreenshotConfig(
    bool Enabled,
    ScreenshotMode Mode,
    int IntervalMinutes,
    int RandomMinMinutes,
    int RandomMaxMinutes);

/// <summary>Activity sampling config (foreground app/window-title; screenshots).</summary>
public sealed record ActivityConfig(int SampleMinutes, ScreenshotConfig Screenshots);

/// <summary>Which SAS reference in the bootstrap secret to use for uploads.</summary>
public sealed record AzureConfig(string LogsSasRef);

/// <summary>
/// The whole agent configuration, as pulled from Azure Storage (YAML). Immutable;
/// the config store swaps a new instance in atomically when a newer version loads.
/// </summary>
public sealed record AgentConfig(
    string Device,
    int Version,
    int PollMinutes,
    int UploadMinutes,
    string TimeZone,
    IReadOnlyList<AppRule> Apps,
    UrlRules Urls,
    ActivityConfig Activity,
    AzureConfig Azure);
