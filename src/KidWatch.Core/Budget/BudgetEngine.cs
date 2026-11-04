using KidWatch.Core.Configuration;

namespace KidWatch.Core.Budget;

/// <summary>The budget state of a monitored app at a moment in time.</summary>
public enum BudgetVerdict
{
    /// <summary>Within budget and schedule; leave it running.</summary>
    Allowed,
    /// <summary>Close to the daily limit; warn the user.</summary>
    Warn,
    /// <summary>Over budget or outside the allowed schedule; should be stopped.</summary>
    Block
}

/// <summary>Outcome of evaluating one <see cref="AppRule"/> against today's usage.</summary>
public sealed record BudgetDecision(
    BudgetVerdict Verdict,
    string Reason,
    int UsedMinutes,
    int LimitMinutes)
{
    /// <summary>
    /// Whether the agent should terminate the process now: only when blocked AND
    /// the rule's action is Kill. A Warn-action rule never kills, it only toasts.
    /// </summary>
    public bool ShouldKill(EnforcementAction action) =>
        Verdict == BudgetVerdict.Block && action == EnforcementAction.Kill;
}

/// <summary>
/// Pure decision logic for app time budgets. No clock, no process access, no I/O —
/// everything it needs is passed in, which makes it exhaustively testable.
/// </summary>
public static class BudgetEngine
{
    public static BudgetDecision Evaluate(AppRule rule, int usedMinutes, DateTime localNow)
    {
        if (rule.Schedule is { } schedule && !schedule.IsOpen(localNow))
            return new(BudgetVerdict.Block, "outside allowed schedule", usedMinutes, rule.DailyLimitMinutes);

        if (usedMinutes >= rule.DailyLimitMinutes)
            return new(BudgetVerdict.Block, "daily limit reached", usedMinutes, rule.DailyLimitMinutes);

        if (usedMinutes >= rule.DailyLimitMinutes - rule.WarnMinutes)
            return new(BudgetVerdict.Warn, "approaching daily limit", usedMinutes, rule.DailyLimitMinutes);

        return new(BudgetVerdict.Allowed, "within budget", usedMinutes, rule.DailyLimitMinutes);
    }
}
