using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace NadekoBot.Modules.Owner.Services;

// ATTACH, VACUUM INTO and PRAGMA can write files, so owner SQL gets its own restricted connection.
public static class OwnerSqlConnection
{
    public const int COMMAND_TIMEOUT_SECONDS = 60;

    private static readonly delegate_authorizer _authorizer = static (_, action, _, _, _, _)
        => action is raw.SQLITE_ATTACH or raw.SQLITE_DETACH or raw.SQLITE_PRAGMA
            ? raw.SQLITE_DENY
            : raw.SQLITE_OK;

    // Unpooled, so the restrictions end when the connection is disposed and no bot query ever gets it.
    public static SqliteConnection Open(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString) { Pooling = false };
        var conn = new SqliteConnection(builder.ToString());
        try
        {
            conn.Open();
            DbPragmas.ApplyRuntime(conn);

            var handle = conn.Handle ?? throw new InvalidOperationException("SQLite connection has no handle");
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_ATTACHED, 0);
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
}
