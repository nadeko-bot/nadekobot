using System.Data.Common;

namespace NadekoBot.Db;

internal static class DbPragmas
{
    private const string SETUP_PRAGMAS = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;";
    private const string RUNTIME_PRAGMAS = "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";

    public static Task ApplySetupAsync(DbConnection conn)
        => ExecuteAsync(conn, SETUP_PRAGMAS);

    public static void ApplyRuntime(DbConnection conn)
        => Execute(conn, RUNTIME_PRAGMAS);

    private static async Task ExecuteAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static void Execute(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
