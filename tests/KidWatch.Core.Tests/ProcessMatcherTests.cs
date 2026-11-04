using KidWatch.Core.Configuration;
using KidWatch.Core.Processes;
using Xunit;

namespace KidWatch.Core.Tests;

public class ProcessMatcherTests
{
    private static AppRule Rule(params string[] match) =>
        new("Roblox", match, 90, 10, EnforcementAction.Kill, null);

    [Theory]
    [InlineData("RobloxPlayerBeta.exe", "RobloxPlayerBeta")]   // config has .exe, OS does not
    [InlineData("RobloxPlayerBeta", "RobloxPlayerBeta.exe")]   // the reverse
    [InlineData("robloxplayerbeta.exe", "RobloxPlayerBeta")]   // case-insensitive
    public void Matches_regardless_of_extension_or_case(string pattern, string processName)
    {
        Assert.True(ProcessMatcher.Matches(Rule(pattern), processName));
    }

    [Fact]
    public void Does_not_match_a_different_process()
    {
        Assert.False(ProcessMatcher.Matches(Rule("RobloxPlayerBeta.exe"), "chrome"));
    }

    [Fact]
    public void Matches_any_of_several_patterns()
    {
        var rule = Rule("chrome.exe", "msedge.exe", "firefox.exe");
        Assert.True(ProcessMatcher.Matches(rule, "firefox"));
        Assert.False(ProcessMatcher.Matches(rule, "opera"));
    }
}
