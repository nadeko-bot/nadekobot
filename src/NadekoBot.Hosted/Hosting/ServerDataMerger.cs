using System.Collections.Frozen;
using System.Text;
using Microsoft.Data.Sqlite;
using NadekoBot.Db.ServerData;
using SQLitePCL;

namespace NadekoBot.Hosted;

// The export file comes from another bot and is not trusted: it gets a restricted connection,
// only plain tables, and only indexes rebuilt from checked names.
public static class ServerDataMerger
{
    public const string IMPORT_SCHEMA = "imp";
    public const int MAX_INDEXES = 10_000;
    public const int BUSY_TIMEOUT_MS = 30_000;

    private const string OK = "ok";
    private const string UTF8 = "UTF-8";
    private const string SEQUENCE_TABLE = "sqlite_sequence";
    private const string AUTO_INDEX_PREFIX = "sqlite_autoindex_";
    private const string CREATE_TABLE = "CREATE TABLE ";
    private const string MAP_PREFIX = "map_";
    private const string BASE_PARAM = "$base";

    private static readonly delegate_authorizer _authorizer = static (_, action, _, _, _, _)
        => action is raw.SQLITE_ATTACH or raw.SQLITE_DETACH ? raw.SQLITE_DENY : raw.SQLITE_OK;

    public static SqliteConnection OpenStaged(string path)
    {
        var conn = ServerDataExporter.OpenFile(path, SqliteOpenMode.ReadWrite);
        try
        {
            var handle = conn.Handle ?? throw new InvalidOperationException("SQLite connection has no handle");
            raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_ATTACHED, 0);
            if (raw.sqlite3_db_config(handle, raw.SQLITE_DBCONFIG_DEFENSIVE, 1, out _) != raw.SQLITE_OK
                || raw.sqlite3_db_config(handle, raw.SQLITE_DBCONFIG_TRUSTED_SCHEMA, 0, out _) != raw.SQLITE_OK
                || raw.sqlite3_set_authorizer(handle, _authorizer, null) != raw.SQLITE_OK)
                throw new InvalidOperationException("Unable to restrict the staged server export");

            ServerDataExporter.Execute(conn, "PRAGMA journal_mode=MEMORY; PRAGMA cell_size_check=ON;");
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    // Returns an ApiErrors code, or null when the file is a clean export whose indexes are rebuilt.
    public static string? Prepare(SqliteConnection staged)
    {
        if (ScalarText(staged, "PRAGMA integrity_check(1)") != OK || ScalarText(staged, "PRAGMA encoding") != UTF8)
            return ApiErrors.BAD_EXPORT;

        var tables = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = staged.CreateCommand())
        {
            cmd.CommandText = "SELECT type, name, sql FROM sqlite_master";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var name = reader.GetString(1);
                var sql = reader.IsDBNull(2) ? null : reader.GetString(2);

                if (type == "index" && sql is null && name.StartsWith(AUTO_INDEX_PREFIX, StringComparison.Ordinal))
                    continue;

                if (type != "table" || sql is null || !sql.StartsWith(CREATE_TABLE, StringComparison.Ordinal))
                    return ApiErrors.BAD_EXPORT;

                if (name != SEQUENCE_TABLE)
                    tables.Add(name);
            }
        }

        if (!tables.Contains(ServerDataTables.INDEX_LIST_TABLE) || !tables.Contains(ServerDataTables.HISTORY_TABLE))
            return ApiErrors.BAD_EXPORT;

        var indexes = new List<ServerDataIndex>();
        using (var cmd = staged.CreateCommand())
        {
            cmd.CommandText = $"""SELECT "Name", "TableName", "Sql" FROM "{ServerDataTables.INDEX_LIST_TABLE}" LIMIT {MAX_INDEXES + 1}""";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (indexes.Count == MAX_INDEXES
                    || reader.GetValue(0) is not string name
                    || reader.GetValue(1) is not string table
                    || reader.GetValue(2) is not string sql
                    || ServerDataIndex.TryParse(sql) is not { } index
                    || index.Name != name
                    || index.Table != table
                    || !tables.Contains(table))
                    return ApiErrors.BAD_EXPORT;

                indexes.Add(index);
            }
        }

        foreach (var index in indexes)
            ServerDataExporter.Execute(staged, index.ToSql());

        ServerDataExporter.Execute(staged, $"""DROP TABLE "{ServerDataTables.INDEX_LIST_TABLE}" """);
        return null;
    }

    public static string? ReadSchema(SqliteConnection staged)
        => ScalarText(staged, $"""SELECT MAX("MigrationId") FROM "{ServerDataTables.HISTORY_TABLE}" """);

    // The columns of each exported table in the live database, or null when the migrated export has other columns.
    public static FrozenDictionary<string, string[]>? MatchColumns(SqliteConnection live, SqliteConnection staged)
    {
        var columns = new Dictionary<string, string[]>(ServerDataTables.All.Length, StringComparer.Ordinal);
        foreach (var table in ServerDataTables.All)
        {
            var liveColumns = ReadColumnsInternal(live, table.Name);
            var stagedColumns = ReadColumnsInternal(staged, table.Name);
            if (liveColumns is null || stagedColumns is null || liveColumns.Length == 0
                || liveColumns.Length != stagedColumns.Length)
                return null;

            foreach (var column in liveColumns)
            {
                if (Array.IndexOf(stagedColumns, column) < 0)
                    return null;
            }

            columns[table.Name] = liveColumns;
        }

        return columns.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // Null when a column is hidden or generated: its value would come from the export's own schema.
    private static string[]? ReadColumnsInternal(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, hidden FROM pragma_table_xinfo($t) ORDER BY cid";
        cmd.Parameters.AddWithValue("$t", table);
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            if (reader.GetInt64(1) != 0)
                return null;

            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    public static SqliteConnection OpenLive(string livePath, SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        var conn = ServerDataExporter.OpenFile(livePath, mode);
        ServerDataExporter.Execute(conn,
            $"PRAGMA busy_timeout={BUSY_TIMEOUT_MS}; PRAGMA trusted_schema=OFF; PRAGMA foreign_keys=ON;");
        return conn;
    }

    // Replaces the guild's rows in the live database with the rows of the export, in one transaction.
    // Rows of parent tables get new ids above the live ones, and their children follow through temp maps.
    public static long Merge(
        string livePath,
        string stagedPath,
        ulong guildId,
        bool includeXp,
        FrozenDictionary<string, string[]> columns)
    {
        using var conn = OpenLive(livePath);
        using (var attach = conn.CreateCommand())
        {
            attach.CommandText = $"ATTACH DATABASE $path AS {IMPORT_SCHEMA}";
            attach.Parameters.AddWithValue("$path", stagedPath);
            attach.ExecuteNonQuery();
        }

        // On an error the connection closes, which rolls back, detaches and drops the temp maps.
        ServerDataExporter.Execute(conn, "BEGIN IMMEDIATE");

        using var cmd = conn.CreateCommand();
        cmd.Parameters.AddWithValue(ServerDataExporter.GUILD_PARAM, (long)guildId);
        var baseParam = cmd.Parameters.Add(BASE_PARAM, SqliteType.Integer);
        baseParam.Value = 0L;

        var all = ServerDataTables.All;
        for (var i = all.Length - 1; i >= 0; i--)
        {
            var table = all[i];
            if (table.IsXp && !includeXp)
                continue;

            cmd.CommandText = $"""DELETE FROM main."{table.Name}" WHERE {ServerDataTables.Where(table, "main")}""";
            cmd.ExecuteNonQuery();
        }

        long rows = 0;
        foreach (var table in all)
        {
            if (table.IsXp && !includeXp)
                continue;

            var isParent = table.HasIdentity && ServerDataTables.Parents.Contains(table.Name);
            if (isParent)
            {
                cmd.CommandText = $"""
                                   SELECT MAX(
                                       COALESCE((SELECT MAX("{ServerDataTables.ID_COLUMN}") FROM main."{table.Name}"), 0),
                                       COALESCE((SELECT seq FROM main.{SEQUENCE_TABLE} WHERE name = '{table.Name}'), 0))
                                   """;
                baseParam.Value = Convert.ToInt64(cmd.ExecuteScalar());

                cmd.CommandText = $"""
                                   CREATE TEMP TABLE "{MAP_PREFIX}{table.Name}" ("Old" INTEGER PRIMARY KEY, "New" INTEGER NOT NULL);
                                   INSERT INTO temp."{MAP_PREFIX}{table.Name}" ("Old", "New")
                                   SELECT s."{ServerDataTables.ID_COLUMN}", {BASE_PARAM} + ROW_NUMBER() OVER (ORDER BY s."{ServerDataTables.ID_COLUMN}")
                                   FROM {IMPORT_SCHEMA}."{table.Name}" s
                                   WHERE {ServerDataTables.Where(table, IMPORT_SCHEMA, "s")};
                                   """;
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = BuildInsertInternal(table, columns[table.Name], isParent);
            rows += cmd.ExecuteNonQuery();
        }

        ServerDataExporter.Execute(conn, "COMMIT");
        return rows;
    }

    private static string BuildInsertInternal(ServerDataTable table, string[] columns, bool isParent)
    {
        var target = new StringBuilder();
        var select = new StringBuilder();
        var joins = new StringBuilder();
        var joinCount = 0;

        foreach (var column in columns)
        {
            string value;
            if (table.HasIdentity && column == ServerDataTables.ID_COLUMN)
            {
                // Other tables let AUTOINCREMENT give the id.
                if (!isParent)
                    continue;

                value = "m.\"New\"";
                joins.Append($""" JOIN temp."{MAP_PREFIX}{table.Name}" m ON m."Old" = s."{column}" """);
            }
            else if (table.Owner is { } owner && owner.Column == column)
            {
                value = "o.\"New\"";
                joins.Append($""" JOIN temp."{MAP_PREFIX}{owner.Parent}" o ON o."Old" = s."{column}" """);
            }
            else if (FindRefInternal(table, column) is { } parent)
            {
                var alias = "r" + joinCount++;
                value = $"COALESCE({alias}.\"New\", 0)";
                joins.Append($""" LEFT JOIN temp."{MAP_PREFIX}{parent}" {alias} ON {alias}."Old" = s."{column}" """);
            }
            else
            {
                value = $"s.\"{column}\"";
            }

            if (target.Length > 0)
            {
                target.Append(", ");
                select.Append(", ");
            }

            target.Append('"').Append(column).Append('"');
            select.Append(value);
        }

        return $"""
                INSERT INTO main."{table.Name}" ({target})
                SELECT {select} FROM {IMPORT_SCHEMA}."{table.Name}" s{joins}
                WHERE {ServerDataTables.Where(table, IMPORT_SCHEMA, "s")}
                """;
    }

    private static string? FindRefInternal(ServerDataTable table, string column)
    {
        if (table.Refs is not { } refs)
            return null;

        foreach (var r in refs)
        {
            if (r.Column == column)
                return r.Parent;
        }

        return null;
    }

    private static string? ScalarText(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }
}
