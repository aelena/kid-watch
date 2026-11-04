using KidWatch.Core.Budget;
using KidWatch.Core.Configuration;
using Xunit;

namespace KidWatch.Core.Tests;

public class BudgetEngineTests
{
    private static readonly DateTime Noon = new(2026, 11, 2, 12, 0, 0); // a Monday

    private static AppRule Rule(int limit = 90, int warn = 10,
        EnforcementAction action = EnforcementAction.Kill, Schedule? schedule = null) =>
        new("Roblox", ["RobloxPlayerBeta.exe"], limit, warn, action, schedule);

    [Fact]
    public void Under_warn_threshold_is_allowed()
    {
        var d = BudgetEngine.Evaluate(Rule(), usedMinutes: 30, Noon);
        Assert.Equal(BudgetVerdict.Allowed, d.Verdict);
    }

    [Fact]
    public void Inside_warn_band_warns()
    {
        var d = BudgetEngine.Evaluate(Rule(limit: 90, warn: 10), usedMinutes: 85, Noon);
        Assert.Equal(BudgetVerdict.Warn, d.Verdict);
    }

    [Fact]
    public void At_or_over_limit_blocks()
    {
        var d = BudgetEngine.Evaluate(Rule(limit: 90), usedMinutes: 90, Noon);
        Assert.Equal(BudgetVerdict.Block, d.Verdict);
        Assert.Equal("daily limit reached", d.Reason);
    }

    [Fact]
    public void Outside_schedule_blocks_even_with_time_left()
    {
        var schedule = ScheduleParser.Parse(
            new Dictionary<string, List<string>> { ["mon"] = ["16:00-19:00"] }).Value;

        var d = BudgetEngine.Evaluate(Rule(schedule: schedule), usedMinutes: 0, Noon);
        Assert.Equal(BudgetVerdict.Block, d.Verdict);
        Assert.Equal("outside allowed schedule", d.Reason);
    }

    [Fact]
    public void Kill_only_when_blocked_and_action_is_kill()
    {
        var blocked = BudgetEngine.Evaluate(Rule(limit: 90, action: EnforcementAction.Kill), 90, Noon);
        Assert.True(blocked.ShouldKill(EnforcementAction.Kill));

        var warnAction = BudgetEngine.Evaluate(Rule(limit: 90, action: EnforcementAction.Warn), 90, Noon);
        Assert.False(warnAction.ShouldKill(EnforcementAction.Warn));
    }
}
