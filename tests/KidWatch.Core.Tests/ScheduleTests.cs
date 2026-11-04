using KidWatch.Core.Configuration;
using Xunit;

namespace KidWatch.Core.Tests;

public class ScheduleTests
{
    private static DateTime At(DayOfWeek day, int hour, int minute = 0)
    {
        // 2026-11-02 is a Monday; offset to the requested weekday.
        var monday = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Unspecified);
        var delta = ((int)day - (int)DayOfWeek.Monday + 7) % 7;
        return monday.AddDays(delta).AddHours(hour).AddMinutes(minute);
    }

    [Fact]
    public void Range_key_expands_to_each_day()
    {
        var raw = new Dictionary<string, List<string>> { ["mon-fri"] = ["16:00-19:00"] };
        var schedule = ScheduleParser.Parse(raw).Value;

        Assert.True(schedule.IsOpen(At(DayOfWeek.Monday, 17)));
        Assert.True(schedule.IsOpen(At(DayOfWeek.Friday, 18, 59)));
        Assert.False(schedule.IsOpen(At(DayOfWeek.Saturday, 17)));
    }

    [Fact]
    public void Window_is_half_open()
    {
        var raw = new Dictionary<string, List<string>> { ["mon"] = ["16:00-19:00"] };
        var schedule = ScheduleParser.Parse(raw).Value;

        Assert.True(schedule.IsOpen(At(DayOfWeek.Monday, 16, 0)));   // inclusive start
        Assert.False(schedule.IsOpen(At(DayOfWeek.Monday, 19, 0)));  // exclusive end
        Assert.False(schedule.IsOpen(At(DayOfWeek.Monday, 15, 59)));
    }

    [Fact]
    public void Multiple_windows_on_a_day_all_count()
    {
        var raw = new Dictionary<string, List<string>>
        {
            ["sat-sun"] = ["10:00-13:00", "16:00-20:00"]
        };
        var schedule = ScheduleParser.Parse(raw).Value;

        Assert.True(schedule.IsOpen(At(DayOfWeek.Saturday, 11)));
        Assert.False(schedule.IsOpen(At(DayOfWeek.Saturday, 14)));   // gap between windows
        Assert.True(schedule.IsOpen(At(DayOfWeek.Sunday, 17)));
    }

    [Fact]
    public void Empty_schedule_is_open_nowhere_once_built_but_parses_to_empty()
    {
        var schedule = ScheduleParser.Parse(null).Value;
        Assert.Same(Schedule.Empty, schedule);
        Assert.False(schedule.IsOpen(At(DayOfWeek.Monday, 12)));
    }

    [Theory]
    [InlineData("mon-fri")]   // bad window, not bad day
    [InlineData("funday")]    // bad day name
    public void Invalid_input_returns_error(string dayKey)
    {
        var raw = new Dictionary<string, List<string>> { [dayKey] = ["25:00-26:00"] };
        var result = ScheduleParser.Parse(raw);
        Assert.True(result.IsError);
    }
}
