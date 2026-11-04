namespace KidWatch.Core.Time;

/// <summary>Abstraction over the system clock so time logic stays testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Resolves a config time-zone id (IANA or Windows) to a TimeZoneInfo.</summary>
public static class TimeZones
{
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
