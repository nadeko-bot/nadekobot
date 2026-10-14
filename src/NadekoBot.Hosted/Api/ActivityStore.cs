using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace NadekoBot.Hosted;

[StructLayout(LayoutKind.Auto)]
public struct ActivityCounts
{
    public long Commands;
    public long Errors;
    public long Joins;
    public long Leaves;

    public void Add(in ActivityCounts other)
    {
        Commands += other.Commands;
        Errors += other.Errors;
        Joins += other.Joins;
        Leaves += other.Leaves;
    }
}

public readonly record struct HourKey(ulong GuildId, long Hour);

public readonly record struct CommandKey(ulong GuildId, long Day, string Name);

// Collects the counts of one guild from the file and from the counts which wait for the next write.
public sealed class ActivityReportBuilder(long firstDay, int dayCount, long firstKeptDay)
{
    public const int HOURS_PER_DAY = 24;

    public long FirstDay { get; } = firstDay;

    // Rows before it stay only until the next write deletes them, so they do not move the first counted day.
    public long FirstKeptHour { get; } = firstKeptDay * HOURS_PER_DAY;
    public ActivityCounts[] Days { get; } = new ActivityCounts[dayCount];
    public long[] Hours { get; } = new long[HOURS_PER_DAY];
    public Dictionary<string, long> Commands { get; } = new(StringComparer.Ordinal);
    public long? FirstHour { get; private set; }

    public void AddHour(long hour, in ActivityCounts counts)
    {
        SeeHour(hour);

        var day = hour / HOURS_PER_DAY;
        var dayIndex = day - FirstDay;
        if (dayIndex < 0 || dayIndex >= Days.Length)
            return;

        Days[dayIndex].Add(counts);
        Hours[(int)(hour - day * HOURS_PER_DAY)] += counts.Commands;
    }

    public void SeeHour(long hour)
    {
        if (hour < FirstKeptHour)
            return;

        if (FirstHour is not { } first || hour < first)
            FirstHour = hour;
    }

    public void AddCommand(string name, long runs)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(Commands, name, out _);
        slot += runs;
    }
}

public sealed class ActivityBrokenException(string message) : Exception(message);

// data/activity.db. Only the activity worker uses it, so it has one connection and no locks.
public sealed class ActivityStore : IDisposable
{
    public const int MAX_COMMANDS_PER_DAY = 50;
    public const int SCHEMA_VERSION = 1;
    public const string BROKEN_SUFFIX = ".broken";

    private const int SQLITE_CORRUPT = 11;
    private const int SQLITE_NOTADB = 26;

    private static readonly string[] _fileSuffixes = ["", "-wal", "-shm"];

    private const string SCHEMA = """
        CREATE TABLE hourly (
            guild_id INTEGER NOT NULL,
            hour INTEGER NOT NULL,
            commands INTEGER NOT NULL,
            errors INTEGER NOT NULL,
            joins INTEGER NOT NULL,
            leaves INTEGER NOT NULL,
            PRIMARY KEY (guild_id, hour)
        ) WITHOUT ROWID;
        CREATE INDEX ix_hourly_hour ON hourly (hour);
        CREATE TABLE command_runs (
            guild_id INTEGER NOT NULL,
            day INTEGER NOT NULL,
            name TEXT NOT NULL,
            runs INTEGER NOT NULL,
            PRIMARY KEY (guild_id, day, name)
        ) WITHOUT ROWID;
        CREATE INDEX ix_command_runs_day ON command_runs (day);
        PRAGMA user_version = 1;
        """;

    private const string UPSERT_HOUR = """
        INSERT INTO hourly (guild_id, hour, commands, errors, joins, leaves)
        VALUES ($guild, $hour, $commands, $errors, $joins, $leaves)
        ON CONFLICT (guild_id, hour) DO UPDATE SET
            commands = commands + excluded.commands,
            errors = errors + excluded.errors,
            joins = joins + excluded.joins,
            leaves = leaves + excluded.leaves
        """;

    // The count and the insert are one statement, so the cap holds for any write order.
    private const string UPSERT_COMMAND = """
        INSERT INTO command_runs (guild_id, day, name, runs)
        SELECT $guild, $day, $name, $runs
        WHERE EXISTS (SELECT 1 FROM command_runs WHERE guild_id = $guild AND day = $day AND name = $name)
           OR (SELECT COUNT(*) FROM command_runs WHERE guild_id = $guild AND day = $day) < $max
        ON CONFLICT (guild_id, day, name) DO UPDATE SET runs = runs + excluded.runs
        """;

    private readonly SqliteConnection _conn;
    private readonly SqliteCommand _upsertHour;
    private readonly SqliteCommand _upsertCommand;

    private ActivityStore(SqliteConnection conn)
    {
        _conn = conn;
        _upsertHour = CreateCommandInternal(conn, UPSERT_HOUR, "$guild", "$hour", "$commands", "$errors", "$joins", "$leaves");
        _upsertCommand = CreateCommandInternal(conn, UPSERT_COMMAND, "$guild", "$day", "$name", "$runs", "$max");
        _upsertCommand.Parameters["$max"].Value = MAX_COMMANDS_PER_DAY;
    }

    // Null when the file cannot be used; then the counts stay in memory until the process exits.
    public static ActivityStore? Open(string path)
    {
        try
        {
            return OpenInternal(path);
        }
        catch (Exception ex) when (IsBrokenInternal(ex))
        {
            Log.Warning(ex, "{Path} is broken. The bot keeps it as {Broken} and starts a new file", path, path + BROKEN_SUFFIX);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot open {Path}. Server activity is counted only in memory", path);
            return null;
        }

        try
        {
            MoveBrokenInternal(path);
            return OpenInternal(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot create {Path}. Server activity is counted only in memory", path);
            return null;
        }
    }

    public void Write(Dictionary<HourKey, ActivityCounts> hours, List<KeyValuePair<CommandKey, long>> commands, long firstKeptDay)
    {
        using var tx = _conn.BeginTransaction();
        _upsertHour.Transaction = tx;
        _upsertCommand.Transaction = tx;

        var hp = _upsertHour.Parameters;
        foreach (var (key, counts) in hours)
        {
            hp[0].Value = unchecked((long)key.GuildId);
            hp[1].Value = key.Hour;
            hp[2].Value = counts.Commands;
            hp[3].Value = counts.Errors;
            hp[4].Value = counts.Joins;
            hp[5].Value = counts.Leaves;
            _upsertHour.ExecuteNonQuery();
        }

        var cp = _upsertCommand.Parameters;
        foreach (var (key, runs) in commands)
        {
            cp[0].Value = unchecked((long)key.GuildId);
            cp[1].Value = key.Day;
            cp[2].Value = key.Name;
            cp[3].Value = runs;
            _upsertCommand.ExecuteNonQuery();
        }

        using (var delete = _conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM hourly WHERE hour < $hour; DELETE FROM command_runs WHERE day < $day;";
            delete.Parameters.AddWithValue("$hour", firstKeptDay * ActivityReportBuilder.HOURS_PER_DAY);
            delete.Parameters.AddWithValue("$day", firstKeptDay);
            delete.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void Read(ulong guildId, ActivityReportBuilder report)
    {
        var guild = unchecked((long)guildId);
        using var cmd = _conn.CreateCommand();
        cmd.Parameters.AddWithValue("$guild", guild);
        cmd.Parameters.AddWithValue("$hour", report.FirstDay * ActivityReportBuilder.HOURS_PER_DAY);
        cmd.Parameters.AddWithValue("$day", report.FirstDay);
        cmd.Parameters.AddWithValue("$kept", report.FirstKeptHour);

        cmd.CommandText = "SELECT MIN(hour) FROM hourly WHERE guild_id = $guild AND hour >= $kept";
        if (cmd.ExecuteScalar() is long firstHour)
            report.SeeHour(firstHour);

        cmd.CommandText = """
            SELECT hour, commands, errors, joins, leaves FROM hourly
            WHERE guild_id = $guild AND hour >= $hour
            """;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var counts = new ActivityCounts
                {
                    Commands = reader.GetInt64(1),
                    Errors = reader.GetInt64(2),
                    Joins = reader.GetInt64(3),
                    Leaves = reader.GetInt64(4)
                };
                report.AddHour(reader.GetInt64(0), counts);
            }
        }

        cmd.CommandText = """
            SELECT name, SUM(runs) FROM command_runs
            WHERE guild_id = $guild AND day >= $day
            GROUP BY name
            """;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                report.AddCommand(reader.GetString(0), reader.GetInt64(1));
        }
    }

    public void Dispose()
    {
        _upsertHour.Dispose();
        _upsertCommand.Dispose();
        _conn.Dispose();
    }

    private static ActivityStore OpenInternal(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        try
        {
            conn.Open();
            ExecuteInternal(conn, "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;");

            var version = (long)(ExecuteScalarInternal(conn, "PRAGMA user_version") ?? 0L);
            if (version > SCHEMA_VERSION)
                throw new InvalidOperationException($"The file has schema version {version}, which a newer bot wrote");

            if (version == 0)
            {
                using var tx = conn.BeginTransaction();
                using var create = conn.CreateCommand();
                create.Transaction = tx;
                create.CommandText = SCHEMA;
                create.ExecuteNonQuery();
                tx.Commit();
            }
            else if (ExecuteScalarInternal(conn, "PRAGMA quick_check") is not "ok")
            {
                throw new ActivityBrokenException("quick_check failed");
            }

            return new ActivityStore(conn);
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private static bool IsBrokenInternal(Exception ex)
        => ex is ActivityBrokenException
           || ex is SqliteException { SqliteErrorCode: SQLITE_CORRUPT or SQLITE_NOTADB };

    private static void MoveBrokenInternal(string path)
    {
        foreach (var suffix in _fileSuffixes)
        {
            var file = path + suffix;
            if (File.Exists(file))
                File.Move(file, path + BROKEN_SUFFIX + suffix, overwrite: true);
        }
    }

    private static SqliteCommand CreateCommandInternal(SqliteConnection conn, string sql, params ReadOnlySpan<string> names)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var name in names)
            cmd.Parameters.Add(new SqliteParameter(name, null));

        return cmd;
    }

    private static void ExecuteInternal(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? ExecuteScalarInternal(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
