using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db.ServerData;

public readonly record struct ServerExportStats(string Schema, long Rows, long XpRows);

public static class ServerDataExporter
{
    public const string DB_ENTRY = "server.db";
    public const string MANIFEST_ENTRY = "manifest.json";
    public const string GUILD_PARAM = "$g";

    private const string SOURCE = "src";
    private const string TABLE_TYPE = "table";
    private const int BUFFER_SIZE = 81920;

    // The exact file which the bot uses; a relative path in creds.yml can resolve to another folder.
    public static string GetLiveDbPath(DbService db)
    {
        using var ctx = db.GetDbContext();
        var conn = (SqliteConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        return conn.DataSource;
    }

    public static SqliteConnection OpenFile(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        }.ToString());

        conn.Open();
        return conn;
    }

    // The new file has every table of the live database, but only rows of the guild, and no indexes.
    // The migrations of a newer bot then run on it as on the live database.
    public static ServerExportStats Build(string liveDbPath, ulong guildId, string destPath)
    {
        using var conn = OpenFile(destPath);
        Execute(conn, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA busy_timeout=30000; PRAGMA trusted_schema=OFF;");

        using (var attach = conn.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE $path AS {SOURCE}";
            attach.Parameters.AddWithValue("$path", liveDbPath);
            attach.ExecuteNonQuery();
        }

        // On an error the connection closes, which rolls back and detaches.
        var indexes = CopySchemaInternal(conn);

        // One transaction reads all tables from one snapshot of the live database.
        Execute(conn, "BEGIN");
        var stats = CopyRowsInternal(conn, guildId, indexes);
        Execute(conn, "COMMIT");

        Execute(conn, $"DETACH DATABASE {SOURCE}");
        Execute(conn, "VACUUM");
        return stats;
    }

    public static void DropXp(string exportDbPath)
    {
        using var conn = OpenFile(exportDbPath, SqliteOpenMode.ReadWrite);
        Execute(conn, $"DELETE FROM \"{ServerDataTables.XP_TABLE}\"; VACUUM;");
    }

    public static void WriteZip(string exportDbPath, ServerExportManifest manifest, string zipPath)
    {
        using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_SIZE);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        using (var manifestStream = zip.CreateEntry(MANIFEST_ENTRY, CompressionLevel.Optimal).Open())
            manifest.WriteTo(manifestStream);

        using var dbStream = zip.CreateEntry(DB_ENTRY, CompressionLevel.Optimal).Open();
        using var source = new FileStream(exportDbPath, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE);
        source.CopyTo(dbStream);
    }

    private static List<ServerDataIndex> CopySchemaInternal(SqliteConnection conn)
    {
        var tables = new List<string>();
        var indexes = new List<ServerDataIndex>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                               SELECT type, sql FROM {SOURCE}.sqlite_master
                               WHERE sql IS NOT NULL AND type IN ('table', 'index') AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
                               ORDER BY rowid
                               """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sql = reader.GetString(1);
                if (reader.GetString(0) == TABLE_TYPE)
                    tables.Add(sql);
                else if (ServerDataIndex.TryParse(sql) is { } index)
                    indexes.Add(index);
            }
        }

        foreach (var sql in tables)
            Execute(conn, sql);

        Execute(conn, $"""CREATE TABLE "{ServerDataTables.INDEX_LIST_TABLE}" ("Name" TEXT NOT NULL, "TableName" TEXT NOT NULL, "Sql" TEXT NOT NULL)""");
        return indexes;
    }

    private static ServerExportStats CopyRowsInternal(SqliteConnection conn, ulong guildId, List<ServerDataIndex> indexes)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""INSERT INTO "{ServerDataTables.INDEX_LIST_TABLE}" ("Name", "TableName", "Sql") VALUES ($n, $t, $s)""";
            var name = cmd.Parameters.Add("$n", SqliteType.Text);
            var table = cmd.Parameters.Add("$t", SqliteType.Text);
            var sql = cmd.Parameters.Add("$s", SqliteType.Text);
            foreach (var index in indexes)
            {
                name.Value = index.Name;
                table.Value = index.Table;
                sql.Value = index.ToSql();
                cmd.ExecuteNonQuery();
            }
        }

        var history = ServerDataTables.HISTORY_TABLE;
        Execute(conn, $"""INSERT INTO main."{history}" SELECT * FROM {SOURCE}."{history}" """);

        long rows = 0, xpRows = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Parameters.AddWithValue(GUILD_PARAM, (long)guildId);
            foreach (var t in ServerDataTables.All)
            {
                cmd.CommandText = $"""
                                   INSERT INTO main."{t.Name}"
                                   SELECT * FROM {SOURCE}."{t.Name}"
                                   WHERE {ServerDataTables.Where(t, "main")}
                                   """;
                var copied = cmd.ExecuteNonQuery();
                rows += copied;
                if (t.IsXp)
                    xpRows = copied;

                if (t.NullOnExport is { } columns)
                {
                    foreach (var column in columns)
                        Execute(conn, $"""UPDATE main."{t.Name}" SET "{column}" = NULL""");
                }
            }
        }

        using var schemaCmd = conn.CreateCommand();
        schemaCmd.CommandText = $"""SELECT MAX("MigrationId") FROM main."{history}" """;
        var schema = schemaCmd.ExecuteScalar() as string ?? string.Empty;

        return new(schema, rows, xpRows);
    }

    public static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
