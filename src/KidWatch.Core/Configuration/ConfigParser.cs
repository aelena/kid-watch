using KidWatch.Core.Functional;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KidWatch.Core.Configuration;

/// <summary>
/// Parses the agent YAML into an immutable <see cref="AgentConfig"/>, returning a
/// <see cref="Result{T}"/> rather than throwing, so the config poller can keep
/// last-known-good on a bad download.
/// </summary>
public static class ConfigParser
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static Result<AgentConfig> Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return Result<AgentConfig>.Fail("Config is empty.");

        RawConfig? raw;
        try
        {
            raw = Yaml.Deserialize<RawConfig>(yaml);
        }
        catch (Exception ex)
        {
            return Result<AgentConfig>.Fail($"YAML parse error: {ex.Message}");
        }

        if (raw is null)
            return Result<AgentConfig>.Fail("Config parsed to nothing.");

        if (string.IsNullOrWhiteSpace(raw.Device))
            return Result<AgentConfig>.Fail("Config is missing 'device'.");

        var apps = new List<AppRule>();
        foreach (var a in raw.Apps ?? [])
        {
            var mapped = MapApp(a);
            if (mapped.IsError) return Result<AgentConfig>.Fail(mapped.Error);
            apps.Add(mapped.Value);
        }

        var urls = MapUrls(raw.Urls);
        if (urls.IsError) return Result<AgentConfig>.Fail(urls.Error);

        var activity = MapActivity(raw.Activity);

        var config = new AgentConfig(
            Device: raw.Device!.Trim(),
            Version: raw.Version,
            PollMinutes: raw.PollMinutes > 0 ? raw.PollMinutes : 15,
            UploadMinutes: raw.UploadMinutes > 0 ? raw.UploadMinutes : 5,
            TimeZone: string.IsNullOrWhiteSpace(raw.Timezone) ? "UTC" : raw.Timezone!.Trim(),
            Apps: apps,
            Urls: urls.Value,
            Activity: activity,
            Azure: new AzureConfig(raw.Azure?.LogsSasRef?.Trim() ?? "logs"));

        return Result<AgentConfig>.Ok(config);
    }

    private static Result<AppRule> MapApp(RawApp a)
    {
        if (string.IsNullOrWhiteSpace(a.Name))
            return Result<AppRule>.Fail("An app rule is missing 'name'.");
        if (a.Match is null || a.Match.Count == 0)
            return Result<AppRule>.Fail($"App rule '{a.Name}' has no 'match' patterns.");
        if (a.DailyLimitMinutes <= 0)
            return Result<AppRule>.Fail($"App rule '{a.Name}' needs a positive 'daily_limit_minutes'.");

        var action = ParseAction(a.Action);
        if (action.IsError) return Result<AppRule>.Fail($"App rule '{a.Name}': {action.Error}");

        var schedule = ScheduleParser.Parse(a.Schedule);
        if (schedule.IsError) return Result<AppRule>.Fail($"App rule '{a.Name}': {schedule.Error}");

        var warn = Math.Max(0, a.WarnMinutes);
        return Result<AppRule>.Ok(new AppRule(
            a.Name!.Trim(), a.Match, a.DailyLimitMinutes, warn, action.Value,
            schedule.Value.Windows.Count == 0 ? null : schedule.Value));
    }

    private static Result<UrlRules> MapUrls(RawUrls? u)
    {
        if (u is null)
            return Result<UrlRules>.Ok(new UrlRules([], []));

        var scheduled = new List<UrlScheduleRule>();
        foreach (var s in u.Schedule ?? [])
        {
            if (string.IsNullOrWhiteSpace(s.Domain))
                return Result<UrlRules>.Fail("A url schedule rule is missing 'domain'.");
            var allow = ScheduleParser.Parse(s.Allow);
            if (allow.IsError) return Result<UrlRules>.Fail($"url '{s.Domain}': {allow.Error}");
            scheduled.Add(new UrlScheduleRule(s.Domain!.Trim(), allow.Value));
        }

        return Result<UrlRules>.Ok(new UrlRules(u.Deny ?? [], scheduled));
    }

    private static ActivityConfig MapActivity(RawActivity? a)
    {
        var sample = a?.SampleMinutes is > 0 ? a.SampleMinutes : 2;
        var s = a?.Screenshots;
        var shots = new ScreenshotConfig(
            Enabled: s?.Enabled ?? false,
            Mode: ParseMode(s?.Mode),
            IntervalMinutes: s?.IntervalMinutes is > 0 ? s.IntervalMinutes : 15,
            RandomMinMinutes: s?.RandomMinMinutes is > 0 ? s.RandomMinMinutes : 10,
            RandomMaxMinutes: s?.RandomMaxMinutes is > 0 ? s.RandomMaxMinutes : 40);
        return new ActivityConfig(sample, shots);
    }

    private static Result<EnforcementAction> ParseAction(string? s) =>
        (s?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "warn" => Result<EnforcementAction>.Ok(EnforcementAction.Warn),
            "kill" => Result<EnforcementAction>.Ok(EnforcementAction.Kill),
            _ => Result<EnforcementAction>.Fail($"unknown action '{s}' (expected warn|kill).")
        };

    private static ScreenshotMode ParseMode(string? s) =>
        s?.Trim().ToLowerInvariant() == "random" ? ScreenshotMode.Random : ScreenshotMode.Fixed;

    // ---- YAML DTOs (loose; mapped + validated above) ----

    private sealed class RawConfig
    {
        public string? Device { get; set; }
        public int Version { get; set; }
        public int PollMinutes { get; set; }
        public int UploadMinutes { get; set; }
        public string? Timezone { get; set; }
        public List<RawApp>? Apps { get; set; }
        public RawUrls? Urls { get; set; }
        public RawActivity? Activity { get; set; }
        public RawAzure? Azure { get; set; }
    }

    private sealed class RawApp
    {
        public string? Name { get; set; }
        public List<string>? Match { get; set; }
        public int DailyLimitMinutes { get; set; }
        public int WarnMinutes { get; set; }
        public string? Action { get; set; }
        public Dictionary<string, List<string>>? Schedule { get; set; }
    }

    private sealed class RawUrls
    {
        public List<string>? Deny { get; set; }
        public List<RawUrlSchedule>? Schedule { get; set; }
    }

    private sealed class RawUrlSchedule
    {
        public string? Domain { get; set; }
        public Dictionary<string, List<string>>? Allow { get; set; }
    }

    private sealed class RawActivity
    {
        public int SampleMinutes { get; set; }
        public RawScreenshots? Screenshots { get; set; }
    }

    private sealed class RawScreenshots
    {
        public bool Enabled { get; set; }
        public string? Mode { get; set; }
        public int IntervalMinutes { get; set; }
        public int RandomMinMinutes { get; set; }
        public int RandomMaxMinutes { get; set; }
    }

    private sealed class RawAzure
    {
        public string? LogsSasRef { get; set; }
    }
}
