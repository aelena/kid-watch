using Microsoft.Data.Sqlite;

namespace KidWatch.Agent.Counters;

/// <summary>
/// Per-day usage counters (minutes spent per app key). Persisted so a reboot does
/// not reset the day's tally. Keyed by (key, day); day is a local ISO date.
/// </summary>
public interface ICounterStore
{
    int GetMinutes(string key, DateOnly day);
    void AddMinutes(string key, DateOnly day, int minutes);
    /// <summary>Housekeeping: drop rows for days strictly before <paramref name="day"/>.</summary>
    void DeleteBefore(DateOnly day);
}

public sealed class SqliteCounterStore : ICounterStore, IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly Lock _gate = new();

    public SqliteCounterStore(AgentOptions options)
    {
        Directory.CreateDirectory(options.DataDir);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = options.DbFile
        }.ToString());
        _conn.Open();

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS counters (
                key     TEXT    NOT NULL,
                day     TEXT    NOT NULL,
                minutes INTEGER NOT NULL,
                PRIMARY KEY (key, day)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public int GetMinutes(string key, DateOnly day)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT minutes FROM counters WHERE key = $k AND day = $d;";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$d", Iso(day));
            var result = cmd.ExecuteScalar();
            return result is long m ? (int)m : 0;
        }
    }

    public void AddMinutes(string key, DateOnly day, int minutes)
    {
        if (minutes == 0) return;
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO counters (key, day, minutes) VALUES ($k, $d, $m)
                ON CONFLICT (key, day) DO UPDATE SET minutes = minutes + $m;
                """;
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$d", Iso(day));
            cmd.Parameters.AddWithValue("$m", minutes);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteBefore(DateOnly day)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM counters WHERE day < $d;";
            cmd.Parameters.AddWithValue("$d", Iso(day));
            cmd.ExecuteNonQuery();
        }
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd");

    public void Dispose() => _conn.Dispose();
}
