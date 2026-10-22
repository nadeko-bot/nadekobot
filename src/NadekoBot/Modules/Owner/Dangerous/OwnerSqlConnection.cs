using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace NadekoBot.Modules.Owner.Services;

// ATTACH, VACUUM INTO and PRAGMA can write files, so owner SQL gets its own restricted connection.
public static class OwnerSqlConnection
{
    public const int COMMAND_TIMEOUT_SECONDS = 60;

    // SQLite holds every value of a row at once, so these two limits cap the memory of one row at 25 MiB.
    public const int READ_MAX_VALUE_BYTES = 256 * 1024;
    public const int READ_MAX_COLUMNS = 100;

    public const int READ_MAX_SQL_LENGTH = 8 * 1024;

    // Shorter than the time limit, so a locked database cannot hold a read past it.
    private const int READ_BUSY_TIMEOUT_MS = 5000;

    // A limited write connection can make the database at most this much larger.
    public const long EXEC_MAX_GROWTH_BYTES = 256L * 1024 * 1024;
    private const int EXEC_MAX_VALUE_BYTES = 16 * 1024 * 1024;

    // The progress handler checks the time limit after this many virtual machine steps.
    private const int PROGRESS_STEPS = 1000;

    private static readonly delegate_authorizer _authorizer = static (_, action, _, arg2, _, _)
        => action switch
        {
            raw.SQLITE_ATTACH or raw.SQLITE_DETACH or raw.SQLITE_PRAGMA => raw.SQLITE_DENY,
            raw.SQLITE_FUNCTION when IsBlockedFunction(arg2.utf8_to_string()) => raw.SQLITE_DENY,
            _ => raw.SQLITE_OK
        };

    // A read connection allows only reads. It refuses every other action, including one which SQLite adds later.
    private static readonly delegate_authorizer _readAuthorizer = static (_, action, _, arg2, _, _)
        => action switch
        {
            raw.SQLITE_SELECT or raw.SQLITE_READ or raw.SQLITE_RECURSIVE => raw.SQLITE_OK,
            raw.SQLITE_FUNCTION => IsBlockedFunction(arg2.utf8_to_string()) ? raw.SQLITE_DENY : raw.SQLITE_OK,
            _ => raw.SQLITE_DENY
        };

    private static readonly delegate_progress _deadline = static state
        => Stopwatch.GetTimestamp() > ((Deadline)state).At ? 1 : 0;

    private sealed class Deadline(long at)
    {
        public readonly long At = at;
    }

    // Unpooled, so the restrictions end when the connection is disposed and no bot query ever gets it.
    // With a time limit, SQLite stops the running statement at the limit and rolls back only that statement,
    // and the connection also caps how much the database and one value can grow.
    public static SqliteConnection Open(string connectionString, TimeSpan? timeLimit)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString) { Pooling = false };
        var conn = new SqliteConnection(builder.ToString());
        try
        {
            conn.Open();
            DbPragmas.ApplyRuntime(conn);

            var handle = conn.Handle ?? throw new InvalidOperationException("Unable to restrict owner SQL");
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_ATTACHED, 0);
            if (timeLimit is { } limit)
            {
                SetDeadlineInternal(handle, limit);
                raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LENGTH, EXEC_MAX_VALUE_BYTES);
                CapGrowthInternal(conn);
            }

            if (raw.sqlite3_set_authorizer(handle, _authorizer, null) != raw.SQLITE_OK)
                throw new InvalidOperationException("Unable to restrict owner SQL");

            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    // A connection which cannot change the database or reach other files, and which stops each query at the time limit.
    public static SqliteConnection OpenReadOnly(string connectionString, TimeSpan timeLimit)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly
        };

        var conn = new SqliteConnection(builder.ToString());
        try
        {
            conn.Open();
            DbPragmas.ApplyRuntime(conn);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA query_only = ON; PRAGMA busy_timeout = {READ_BUSY_TIMEOUT_MS};";
                cmd.ExecuteNonQuery();
            }

            var handle = conn.Handle ?? throw new InvalidOperationException("Unable to restrict owner SQL");
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_ATTACHED, 0);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LENGTH, READ_MAX_VALUE_BYTES);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COLUMN, READ_MAX_COLUMNS);
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_SQL_LENGTH, READ_MAX_SQL_LENGTH);

            SetDeadlineInternal(handle, timeLimit);

            if (raw.sqlite3_set_authorizer(handle, _readAuthorizer, null) != raw.SQLITE_OK)
                throw new InvalidOperationException("Unable to restrict owner SQL");

            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    // Microsoft.Data.Sqlite runs the statements after the first only when the reader asks for more results,
    // so the bot refuses such a query before it runs.
    public static bool IsSingleStatement(SqliteConnection conn, string sql)
    {
        // SQLite stops reading the text at a NUL, so the rest would never get shorter.
        if (sql.Contains('\0'))
            return false;

        var count = 0;
        var rest = sql;
        while (!string.IsNullOrWhiteSpace(rest))
        {
            var rc = raw.sqlite3_prepare_v2(conn.Handle, rest, out var stmt, out var tail);
            using (stmt)
            {
                if (rc != raw.SQLITE_OK && count > 0)
                    return false;

                SqliteException.ThrowExceptionForRC(rc, conn.Handle);
                if (stmt is { IsInvalid: false } && ++count > 1)
                    return false;
            }

            if (tail.Length >= rest.Length)
                return false;

            rest = tail;
        }

        return count == 1;
    }

    // max_page_count holds only for this connection, so the bot's own writes are not limited.
    private static void CapGrowthInternal(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT page_count, page_size FROM pragma_page_count(), pragma_page_size();";
        long pages, pageSize;
        using (var reader = cmd.ExecuteReader())
        {
            reader.Read();
            pages = reader.GetInt64(0);
            pageSize = reader.GetInt64(1);
        }

        cmd.CommandText = $"PRAGMA max_page_count = {pages + (EXEC_MAX_GROWTH_BYTES / pageSize)};";
        cmd.ExecuteScalar();
    }

    private static void SetDeadlineInternal(sqlite3 handle, TimeSpan timeLimit)
    {
        var at = Stopwatch.GetTimestamp() + (long)(timeLimit.TotalSeconds * Stopwatch.Frequency);
        raw.sqlite3_progress_handler(handle, PROGRESS_STEPS, _deadline, new Deadline(at));
    }

    // SQLite function names are ASCII.
    private static bool IsBlockedFunction(string? name)
        => string.Equals(name, "load_extension", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "fts3_tokenizer", StringComparison.OrdinalIgnoreCase);
}
