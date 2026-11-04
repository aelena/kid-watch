using KidWatch.Agent;
using KidWatch.Agent.Configuration;
using KidWatch.Agent.Counters;
using KidWatch.Agent.Enforcement;
using KidWatch.Agent.Logging;
using KidWatch.Agent.Notifications;
using KidWatch.Agent.Processes;
using KidWatch.Core.Configuration;
using KidWatch.Core.Events;
using KidWatch.Core.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KidWatch.Core.Tests;

public class TimeEngineTests
{
    // Monday 2026-11-02, 12:00 UTC.
    private static readonly DateTimeOffset MondayNoon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static TimeEngine Build(
        AgentConfig config, FakeProbe probe, InMemoryCounters counters,
        CapturingSink events, CountingNotifier notifier, FakeClock clock, int tickSeconds = 60)
    {
        var store = new ConfigStore(new AgentOptions());
        store.TrySetIfNewer(config);
        return new TimeEngine(store, counters, probe, events, notifier, clock,
            NullLogger<TimeEngine>.Instance, TimeSpan.FromSeconds(tickSeconds));
    }

    private static AgentConfig Config(AppRule rule, string tz = "UTC") =>
        new("dev", 1, 15, 5, tz, [rule],
            new UrlRules([], []),
            new ActivityConfig(2, new ScreenshotConfig(false, ScreenshotMode.Fixed, 15, 10, 40)),
            new AzureConfig("logs"));

    private static AppRule App(string name, string[] match, int limit, int warn,
        EnforcementAction action, Schedule? schedule = null) =>
        new(name, match, limit, warn, action, schedule);

    [Fact]
    public async Task Running_app_accumulates_then_warns_then_kills()
    {
        var probe = new FakeProbe("RobloxPlayerBeta");
        var counters = new InMemoryCounters();
        var events = new CapturingSink();
        var notifier = new CountingNotifier();
        var clock = new FakeClock(MondayNoon);
        var engine = Build(
            Config(App("Roblox", ["RobloxPlayerBeta.exe"], limit: 2, warn: 1, EnforcementAction.Kill)),
            probe, counters, events, notifier, clock);

        await engine.TickAsync(default); // used = 1 min -> warn band
        Assert.Contains(events.Lines, l => l.Contains("\"event\":\"warned\""));
        Assert.Empty(probe.Killed);

        await engine.TickAsync(default); // used = 2 min -> at limit -> kill
        Assert.Contains("RobloxPlayerBeta", probe.Killed);
        Assert.Contains(events.Lines, l => l.Contains("\"event\":\"killed\""));
    }

    [Fact]
    public async Task Outside_schedule_kills_even_with_time_left()
    {
        var schedule = ScheduleParser.Parse(
            new Dictionary<string, List<string>> { ["mon"] = ["16:00-19:00"] }).Value;
        var probe = new FakeProbe("RobloxPlayerBeta");
        var engine = Build(
            Config(App("Roblox", ["RobloxPlayerBeta.exe"], limit: 90, warn: 10, EnforcementAction.Kill, schedule)),
            probe, new InMemoryCounters(), new CapturingSink(), new CountingNotifier(), new FakeClock(MondayNoon));

        await engine.TickAsync(default); // 12:00 is outside 16-19 -> blocked -> kill

        Assert.Contains("RobloxPlayerBeta", probe.Killed);
    }

    [Fact]
    public async Task Non_matching_process_is_ignored()
    {
        var probe = new FakeProbe("notepad");
        var counters = new InMemoryCounters();
        var events = new CapturingSink();
        var engine = Build(
            Config(App("Roblox", ["RobloxPlayerBeta.exe"], 90, 10, EnforcementAction.Kill)),
            probe, counters, events, new CountingNotifier(), new FakeClock(MondayNoon));

        await engine.TickAsync(default);

        Assert.Equal(0, counters.GetSeconds("Roblox", new DateOnly(2026, 11, 2)));
        Assert.Empty(probe.Killed);
        Assert.Empty(events.Lines);
    }

    [Fact]
    public async Task Warn_action_over_limit_warns_but_does_not_kill()
    {
        var probe = new FakeProbe("chrome");
        var events = new CapturingSink();
        var engine = Build(
            Config(App("Browsers", ["chrome.exe"], limit: 1, warn: 0, EnforcementAction.Warn)),
            probe, new InMemoryCounters(), events, new CountingNotifier(), new FakeClock(MondayNoon));

        await engine.TickAsync(default); // used = 1 min -> over limit, but action = warn

        Assert.Empty(probe.Killed);
        Assert.Contains(events.Lines, l => l.Contains("\"event\":\"warned\""));
    }

    [Fact]
    public async Task Warn_fires_only_once_per_day()
    {
        var probe = new FakeProbe("RobloxPlayerBeta");
        var notifier = new CountingNotifier();
        var engine = Build(
            Config(App("Roblox", ["RobloxPlayerBeta.exe"], limit: 10, warn: 9, EnforcementAction.Kill)),
            probe, new InMemoryCounters(), new CapturingSink(), notifier, new FakeClock(MondayNoon));

        await engine.TickAsync(default); // used 1 -> warn (1 >= 10-9)
        await engine.TickAsync(default); // used 2 -> still warn band, must not notify again

        Assert.Equal(1, notifier.Count);
    }

    // ---- fakes ----

    private sealed class FakeProbe(params string[] names) : IProcessProbe
    {
        private List<RunningProcess> _snapshot =
            names.Select((n, i) => new RunningProcess(1000 + i, n)).ToList();

        public List<string> Killed { get; } = [];

        public IReadOnlyList<RunningProcess> Snapshot() => _snapshot;

        public void KillTree(string processName)
        {
            Killed.Add(processName);
            _snapshot = _snapshot
                .Where(p => !string.Equals(p.Name, processName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private sealed class InMemoryCounters : ICounterStore
    {
        private readonly Dictionary<(string Key, DateOnly Day), int> _d = new();

        public int GetSeconds(string key, DateOnly day) =>
            _d.TryGetValue((key, day), out var s) ? s : 0;

        public void AddSeconds(string key, DateOnly day, int seconds) =>
            _d[(key, day)] = GetSeconds(key, day) + seconds;

        public void DeleteBefore(DateOnly day)
        {
            foreach (var k in _d.Keys.Where(k => k.Day < day).ToList())
                _d.Remove(k);
        }
    }

    private sealed class CapturingSink : IEventSink
    {
        public List<string> Lines { get; } = [];

        public Task WriteAsync(AgentEvent e, CancellationToken ct = default)
        {
            Lines.Add(e.ToJsonLine());
            return Task.CompletedTask;
        }
    }

    private sealed class CountingNotifier : INotifier
    {
        public int Count { get; private set; }
        public void Notify(string title, string message) => Count++;
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
