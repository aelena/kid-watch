using KidWatch.Core.Configuration;

namespace KidWatch.Core.Processes;

/// <summary>
/// Pure matching of a running process against an <see cref="AppRule"/>. Config
/// patterns are exe names ("RobloxPlayerBeta.exe"); the OS reports process names
/// without the extension ("RobloxPlayerBeta"), so both sides are normalised.
/// </summary>
public static class ProcessMatcher
{
    public static string Normalize(string name)
    {
        var n = name.Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        return n.ToLowerInvariant();
    }

    public static bool Matches(AppRule rule, string processName)
    {
        var p = Normalize(processName);
        return rule.Match.Any(m => Normalize(m) == p);
    }
}
