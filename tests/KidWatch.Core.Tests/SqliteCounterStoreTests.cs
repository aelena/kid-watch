using KidWatch.Agent;
using KidWatch.Agent.Counters;
using Xunit;

namespace KidWatch.Core.Tests;

public class SqliteCounterStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "kidwatch-test-" + Guid.NewGuid().ToString("N"));

    private SqliteCounterStore NewStore() =>
        new(new AgentOptions { DataDir = _dir });

    [Fact]
    public void Add_then_get_returns_accumulated_seconds()
    {
        using var store = NewStore();
        var day = new DateOnly(2026, 11, 4);

        Assert.Equal(0, store.GetSeconds("Roblox", day));

        store.AddSeconds("Roblox", day, 15);
        store.AddSeconds("Roblox", day, 45);

        Assert.Equal(60, store.GetSeconds("Roblox", day));
    }

    [Fact]
    public void Counters_are_isolated_per_key_and_day()
    {
        using var store = NewStore();
        var mon = new DateOnly(2026, 11, 2);
        var tue = new DateOnly(2026, 11, 3);

        store.AddSeconds("Roblox", mon, 1800);
        store.AddSeconds("Browsers", mon, 600);
        store.AddSeconds("Roblox", tue, 420);

        Assert.Equal(1800, store.GetSeconds("Roblox", mon));
        Assert.Equal(600, store.GetSeconds("Browsers", mon));
        Assert.Equal(420, store.GetSeconds("Roblox", tue));
    }

    [Fact]
    public void DeleteBefore_drops_older_days_only()
    {
        using var store = NewStore();
        var old = new DateOnly(2026, 10, 1);
        var keep = new DateOnly(2026, 11, 4);

        store.AddSeconds("Roblox", old, 5940);
        store.AddSeconds("Roblox", keep, 660);

        store.DeleteBefore(new DateOnly(2026, 11, 1));

        Assert.Equal(0, store.GetSeconds("Roblox", old));
        Assert.Equal(660, store.GetSeconds("Roblox", keep));
    }

    [Fact]
    public void Counts_survive_reopening_the_database()
    {
        var day = new DateOnly(2026, 11, 4);
        using (var store = NewStore())
            store.AddSeconds("Roblox", day, 2520);

        using var reopened = NewStore();
        Assert.Equal(2520, reopened.GetSeconds("Roblox", day));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
