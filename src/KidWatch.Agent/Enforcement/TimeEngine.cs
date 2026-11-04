using KidWatch.Agent.Configuration;
using KidWatch.Agent.Counters;
using KidWatch.Agent.Notifications;
using KidWatch.Agent.Processes;
using KidWatch.Agent.Logging;
using KidWatch.Core.Budget;
using KidWatch.Core.Configuration;
using KidWatch.Core.Events;
using KidWatch.Core.Processes;
using KidWatch.Core.Time;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Enforcement;

/// <summary>
/// The enforcement core. One <see cref="TickAsync"/> call: resolve local time,
/// roll the day if needed, snapshot running processes, and for each app rule
/// accumulate elapsed seconds, run the pure <see cref="BudgetEngine"/>, and warn
/// (once) or kill the process tree. All side effects go through injected seams,
/// so the whole thing is driven by fakes in tests.
/// </summary>
public sealed class TimeEngine(
    IConfigStore store,
    ICounterStore counters,
    IProcessProbe probe,
    IEventSink events,
    INotifier notifier,
    IClock clock,
    ILogger<TimeEngine> logger,
    TimeSpan interval)
{
    private const int RetentionDays = 30;

    private readonly int _tickSeconds = Math.Max(1, (int)interval.TotalSeconds);

    // Per-day state so we warn once and log a kill once per block episode.
    private DateOnly _day;
    private readonly HashSet<string> _warned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _killing = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan Interval { get; } = interval;

    public async Task TickAsync(CancellationToken ct)
    {
        var config = store.Current;
        if (config is null)
            return; // no config yet; nothing to enforce

        var tz = TimeZones.Resolve(config.TimeZone);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, tz).DateTime;
        var today = DateOnly.FromDateTime(localNow);

        if (today != _day)
        {
            _day = today;
            _warned.Clear();
            _killing.Clear();
            counters.DeleteBefore(today.AddDays(-RetentionDays));
        }

        var snapshot = probe.Snapshot();

        foreach (var rule in config.Apps)
        {
            var running = snapshot.Where(p => ProcessMatcher.Matches(rule, p.Name)).ToList();
            if (running.Count == 0)
            {
                _killing.Remove(rule.Name); // relaunch later should re-log a kill
                continue;
            }

            counters.AddSeconds(rule.Name, today, _tickSeconds);
            var usedMinutes = counters.GetSeconds(rule.Name, today) / 60;
            var decision = BudgetEngine.Evaluate(rule, usedMinutes, localNow);

            switch (decision.Verdict)
            {
                case BudgetVerdict.Warn:
                    await WarnOnce(config.Device, rule, decision, ct);
                    break;

                case BudgetVerdict.Block when decision.ShouldKill(rule.Action):
                    await KillAndLog(config.Device, rule, decision, running, ct);
                    break;

                case BudgetVerdict.Block: // action is Warn: notify, do not kill
                    await WarnOnce(config.Device, rule, decision, ct);
                    break;
            }
        }
    }

    private async Task WarnOnce(string device, AppRule rule, BudgetDecision decision, CancellationToken ct)
    {
        if (!_warned.Add(rule.Name))
            return;

        notifier.Notify($"{rule.Name} time",
            $"{decision.UsedMinutes} of {decision.LimitMinutes} min used today ({decision.Reason}).");
        await events.WriteAsync(
            AgentEvent.Budget(device, rule.Name, "warned", decision.UsedMinutes, decision.LimitMinutes, clock.UtcNow), ct);
        logger.LogInformation("warn {Rule}: {Used}/{Limit} min ({Reason})",
            rule.Name, decision.UsedMinutes, decision.LimitMinutes, decision.Reason);
    }

    private async Task KillAndLog(string device, AppRule rule, BudgetDecision decision,
        List<RunningProcess> running, CancellationToken ct)
    {
        foreach (var name in running.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            probe.KillTree(name);

        if (!_killing.Add(rule.Name))
            return; // already logged this block episode; keep killing silently

        notifier.Notify($"{rule.Name} blocked", $"Closing {rule.Name}: {decision.Reason}.");
        await events.WriteAsync(
            AgentEvent.Budget(device, rule.Name, "killed", decision.UsedMinutes, decision.LimitMinutes, clock.UtcNow), ct);
        logger.LogInformation("kill {Rule}: {Reason} ({Used}/{Limit} min)",
            rule.Name, decision.Reason, decision.UsedMinutes, decision.LimitMinutes);
    }
}
