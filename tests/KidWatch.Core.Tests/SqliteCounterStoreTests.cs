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
    public void Add_then_get_returns_accumulated_minutes()
    {
        using var store = NewStore();
        var day = new DateOnly(2026, 11, 4);

        Assert.Equal(0, store.GetMinutes("Roblox", day));

        store.AddMinutes("Roblox", day, 15);
        store.AddMinutes("Roblox", day, 5);

        Assert.Equal(20, store.GetMinutes("Roblox", day));
    }

    [Fact]
    public void Counters_are_isolated_per_key_and_day()
    {
        using var store = NewStore();
        var mon = new DateOnly(2026, 11, 2);
        var tue = new DateOnly(2026, 11, 3);

        store.AddMinutes("Roblox", mon, 30);
        store.AddMinutes("Browsers", mon, 10);
        store.AddMinutes("Roblox", tue, 7);

        Assert.Equal(30, store.GetMinutes("Roblox", mon));
        Assert.Equal(10, store.GetMinutes("Browsers", mon));
        Assert.Equal(7, store.GetMinutes("Roblox", tue));
    }

    [Fact]
    public void DeleteBefore_drops_older_days_only()
    {
        using var store = NewStore();
        var old = new DateOnly(2026, 10, 1);
        var keep = new DateOnly(2026, 11, 4);

        store.AddMinutes("Roblox", old, 99);
        store.AddMinutes("Roblox", keep, 11);

        store.DeleteBefore(new DateOnly(2026, 11, 1));

        Assert.Equal(0, store.GetMinutes("Roblox", old));
        Assert.Equal(11, store.GetMinutes("Roblox", keep));
    }

    [Fact]
    public void Counts_survive_reopening_the_database()
    {
        var day = new DateOnly(2026, 11, 4);
        using (var store = NewStore())
            store.AddMinutes("Roblox", day, 42);

        using var reopened = NewStore();
        Assert.Equal(42, reopened.GetMinutes("Roblox", day));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
