using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace NadekoBot.Db;

public static class PostgresMigrator
{
    private const string SCHEMA_SCRIPT_PATH = "Migrations/pg2sqlite_schema.sql";
    private const string DEFAULT_SQLITE_CONN_STRING = "Data Source=data/NadekoBot.db";
    private const int PROGRESS_LOG_INTERVAL = 50_000;

    public static async Task MigrateAsync(string pgConnString)
    {
        var sqlitePath = Path.Combine(AppContext.BaseDirectory, "data", "NadekoBot.db");

        if (File.Exists(sqlitePath))
        {
            var backupPath = sqlitePath + ".bak";
            Log.Warning("Existing SQLite database found. Backing up to {BackupPath}", backupPath);
            File.Copy(sqlitePath, backupPath, true);

            File.Delete(sqlitePath);

            var walPath = sqlitePath + "-wal";
            var shmPath = sqlitePath + "-shm";
            if (File.Exists(walPath))
                File.Delete(walPath);
            if (File.Exists(shmPath))
                File.Delete(shmPath);
        }

        var sqliteConnString = new SqliteConnectionStringBuilder(DEFAULT_SQLITE_CONN_STRING)
        {
            DataSource = sqlitePath
        }.ToString();

        await using var sqliteConn = new SqliteConnection(sqliteConnString);
        await sqliteConn.OpenAsync();

        await using (var fkCmd = sqliteConn.CreateCommand())
        {
            fkCmd.CommandText = "PRAGMA foreign_keys = OFF";
            await fkCmd.ExecuteNonQueryAsync();
        }

        Log.Information("Creating SQLite schema...");
        var schemaScript = await File.ReadAllTextAsync(SCHEMA_SCRIPT_PATH);
        await using (var cmd = sqliteConn.CreateCommand())
        {
            cmd.CommandText = schemaScript;
            await cmd.ExecuteNonQueryAsync();
        }

        await using var pgConn = new NpgsqlConnection(pgConnString);
        await pgConn.OpenAsync();

        var tables = await GetPgTablesAsync(pgConn);

        Log.Information("Migrating {Count} tables from PostgreSQL to SQLite...", tables.Count);

        foreach (var pgTable in tables)
        {
            if (string.Equals(pgTable, "__efmigrationshistory", StringComparison.InvariantCultureIgnoreCase))
                continue;

            var sqliteTable = await FindSqliteTableAsync(sqliteConn, pgTable);
            if (sqliteTable is null)
            {
                Log.Warning("Table '{PgTable}' exists in PostgreSQL but not in SQLite schema. Skipping", pgTable);
                continue;
            }

            await MigrateTableInternalAsync(pgConn, sqliteConn, pgTable, sqliteTable);
        }

        await FixAutoIncrementSequencesInternalAsync(sqliteConn);

        Log.Information("PostgreSQL to SQLite migration completed successfully");
    }

    private static async Task<List<string>> GetPgTablesAsync(NpgsqlConnection pgConn)
    {
        var tables = new List<string>();
        await using var cmd = pgConn.CreateCommand();
        cmd.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<string?> FindSqliteTableAsync(SqliteConnection sqliteConn, string pgTable)
    {
        await using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND LOWER(name) = LOWER(@name)";
        cmd.Parameters.AddWithValue("@name", pgTable);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private static async Task<List<(string Name, string Type)>> GetSqliteColumnInfoAsync(
        SqliteConnection conn,
        string tableName)
    {
        var columns = new List<(string Name, string Type)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add((reader.GetString(1), reader.GetString(2).ToUpperInvariant()));
        return columns;
    }

    private static async Task<List<string>> GetPgColumnsAsync(NpgsqlConnection conn, string tableName)
    {
        var columns = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table ORDER BY ordinal_position";
        cmd.Parameters.AddWithValue("@table", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task MigrateTableInternalAsync(
        NpgsqlConnection pgConn,
        SqliteConnection sqliteConn,
        string pgTable,
        string sqliteTable)
    {
        var sqliteColumnInfo = await GetSqliteColumnInfoAsync(sqliteConn, sqliteTable);
        if (sqliteColumnInfo.Count == 0)
            return;

        var pgColumns = await GetPgColumnsAsync(pgConn, pgTable);

        var columnMapping = new List<(string PgCol, string SqliteCol, string SqliteType)>();
        foreach (var pgCol in pgColumns)
        {
            var match = sqliteColumnInfo.FirstOrDefault(
                sc => string.Equals(sc.Name, pgCol, StringComparison.InvariantCultureIgnoreCase));

            if (match.Name is not null)
                columnMapping.Add((pgCol, match.Name, match.Type));
        }

        if (columnMapping.Count == 0)
            return;

        var pgColList = string.Join(", ", columnMapping.Select(static m => $"\"{m.PgCol}\""));
        var sqliteColList = string.Join(", ", columnMapping.Select(static m => $"\"{m.SqliteCol}\""));
        var paramList = string.Join(", ", columnMapping.Select(static (_, i) => $"@p{i}"));

        await using var readCmd = pgConn.CreateCommand();
        readCmd.CommandText = $"SELECT {pgColList} FROM \"{pgTable}\"";
        await using var reader = await readCmd.ExecuteReaderAsync();

        var insertSql = $"INSERT INTO \"{sqliteTable}\" ({sqliteColList}) VALUES ({paramList})";

        await using var transaction = await sqliteConn.BeginTransactionAsync();

        await using var insertCmd = sqliteConn.CreateCommand();
        insertCmd.Transaction = (SqliteTransaction)transaction;
        insertCmd.CommandText = insertSql;

        var parameters = new SqliteParameter[columnMapping.Count];
        for (var i = 0; i < columnMapping.Count; i++)
        {
            parameters[i] = new SqliteParameter($"@p{i}", DBNull.Value);
            insertCmd.Parameters.Add(parameters[i]);
        }

        insertCmd.Prepare();

        var rowCount = 0;

        while (await reader.ReadAsync())
        {
            for (var i = 0; i < columnMapping.Count; i++)
            {
                var value = reader.GetValue(i);
                parameters[i].Value = ConvertPgValueToSqlite(value, columnMapping[i].SqliteType);
            }

            await insertCmd.ExecuteNonQueryAsync();
            rowCount++;

            if (rowCount % PROGRESS_LOG_INTERVAL == 0)
                Log.Information("  {Table}: {Count} rows migrated so far...", pgTable, rowCount);
        }

        await transaction.CommitAsync();

        if (rowCount > 0)
            Log.Information("Migrated {Count} rows from '{PgTable}' to '{SqliteTable}'", rowCount, pgTable, sqliteTable);
    }

    private static object ConvertPgValueToSqlite(object value, string sqliteType)
    {
        if (value is DBNull)
            return DBNull.Value;

        return value switch
        {
            decimal d when sqliteType is "INTEGER" => unchecked((long)(Int128)d),
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            TimeSpan ts => ts.ToString(),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"),
            bool b => b ? 1L : 0L,
            float f => (double)f,
            _ => value
        };
    }

    private static async Task FixAutoIncrementSequencesInternalAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_sequence";

        var tables = new List<string>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }
        catch
        {
            return;
        }

        foreach (var table in tables)
        {
            await using var pkCmd = conn.CreateCommand();
            pkCmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            string? pkCol = null;
            await using (var pkReader = await pkCmd.ExecuteReaderAsync())
            {
                while (await pkReader.ReadAsync())
                {
                    if (pkReader.GetInt32(5) == 1)
                    {
                        pkCol = pkReader.GetString(1);
                        break;
                    }
                }
            }

            if (pkCol is null)
                continue;

            await using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText =
                $"UPDATE sqlite_sequence SET seq = (SELECT MAX(\"{pkCol}\") FROM \"{table}\") WHERE name = @name";
            updateCmd.Parameters.AddWithValue("@name", table);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }
}
