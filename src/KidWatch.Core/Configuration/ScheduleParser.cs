using System.Globalization;
using KidWatch.Core.Functional;

namespace KidWatch.Core.Configuration;

/// <summary>
/// Parses the YAML schedule shape — a map of day-group keys ("mon-fri",
/// "sat-sun", "mon", "daily") to window strings ("16:00-19:00") — into a
/// <see cref="Schedule"/> keyed by <see cref="DayOfWeek"/>.
/// </summary>
public static class ScheduleParser
{
    private static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private static readonly IReadOnlyDictionary<string, DayOfWeek> DayNames =
        new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["mon"] = DayOfWeek.Monday,
            ["tue"] = DayOfWeek.Tuesday,
            ["wed"] = DayOfWeek.Wednesday,
            ["thu"] = DayOfWeek.Thursday,
            ["fri"] = DayOfWeek.Friday,
            ["sat"] = DayOfWeek.Saturday,
            ["sun"] = DayOfWeek.Sunday,
        };

    public static Result<Schedule> Parse(IDictionary<string, List<string>>? raw)
    {
        if (raw is null || raw.Count == 0)
            return Result<Schedule>.Ok(Schedule.Empty);

        var windows = new Dictionary<DayOfWeek, List<TimeWindow>>();

        foreach (var (dayKey, windowStrings) in raw)
        {
            var days = ParseDayKey(dayKey);
            if (days.IsError)
                return Result<Schedule>.Fail(days.Error);

            foreach (var windowString in windowStrings ?? [])
            {
                var window = ParseWindow(windowString);
                if (window.IsError)
                    return Result<Schedule>.Fail(window.Error);

                foreach (var day in days.Value)
                {
                    if (!windows.TryGetValue(day, out var list))
                        windows[day] = list = [];
                    list.Add(window.Value);
                }
            }
        }

        var frozen = windows.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<TimeWindow>)kv.Value);

        return Result<Schedule>.Ok(new Schedule(frozen));
    }

    private static Result<IReadOnlyList<DayOfWeek>> ParseDayKey(string key)
    {
        var k = key.Trim().ToLowerInvariant();

        if (k is "daily" or "everyday" or "all")
            return Result<IReadOnlyList<DayOfWeek>>.Ok(Week);

        if (!k.Contains('-'))
            return DayNames.TryGetValue(k, out var single)
                ? Result<IReadOnlyList<DayOfWeek>>.Ok([single])
                : Result<IReadOnlyList<DayOfWeek>>.Fail($"Unknown day '{key}' in schedule.");

        var parts = k.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !DayNames.TryGetValue(parts[0], out var start)
            || !DayNames.TryGetValue(parts[1], out var end))
            return Result<IReadOnlyList<DayOfWeek>>.Fail($"Invalid day range '{key}' in schedule.");

        var startIdx = Array.IndexOf(Week, start);
        var endIdx = Array.IndexOf(Week, end);
        var span = new List<DayOfWeek>();
        for (var i = startIdx; ; i = (i + 1) % 7)
        {
            span.Add(Week[i]);
            if (i == endIdx) break;
        }
        return Result<IReadOnlyList<DayOfWeek>>.Ok(span);
    }

    private static Result<TimeWindow> ParseWindow(string spec)
    {
        var parts = (spec ?? "").Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !TryTime(parts[0], out var start)
            || !TryTime(parts[1], out var end))
            return Result<TimeWindow>.Fail($"Invalid time window '{spec}' (expected HH:mm-HH:mm).");

        if (end <= start)
            return Result<TimeWindow>.Fail($"Time window '{spec}' ends before it starts.");

        return Result<TimeWindow>.Ok(new TimeWindow(start, end));
    }

    private static bool TryTime(string s, out TimeOnly time) =>
        TimeOnly.TryParseExact(s, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out time);
}
