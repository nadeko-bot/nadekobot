using Microsoft.Data.Sqlite;
using Npgsql;

namespace NadekoBot.Db;

public static class PostgresMigrator
{
    private const string SCHEMA_SCRIPT_PATH = "Migrations/pg2sqlite_schema.sql";
    private const string DEFAULT_SQLITE_CONN_STRING = "Data Source=data/NadekoBot.db";

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

            await MigrateTableAsync(pgConn, sqliteConn, pgTable, sqliteTable);
        }

        await FixAutoIncrementSequencesAsync(sqliteConn);

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

    private static async Task MigrateTableAsync(
        NpgsqlConnection pgConn,
        SqliteConnection sqliteConn,
        string pgTable,
        string sqliteTable)
    {
        var sqliteColumns = await GetSqliteColumnsAsync(sqliteConn, sqliteTable);
        if (sqliteColumns.Count == 0)
            return;

        var pgColumns = await GetPgColumnsAsync(pgConn, pgTable);

        // build mapping: pg column name -> sqlite column name (case-insensitive match)
        var columnMapping = new List<(string PgCol, string SqliteCol)>();
        foreach (var pgCol in pgColumns)
        {
            var match = sqliteColumns.FirstOrDefault(
                sc => string.Equals(sc, pgCol, StringComparison.InvariantCultureIgnoreCase));

            if (match is not null)
                columnMapping.Add((pgCol, match));
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
        var rowCount = 0;

        while (await reader.ReadAsync())
        {
            await using var insertCmd = sqliteConn.CreateCommand();
            insertCmd.Transaction = (SqliteTransaction)transaction;
            insertCmd.CommandText = insertSql;

            for (var i = 0; i < columnMapping.Count; i++)
            {
                var value = reader.GetValue(i);
                insertCmd.Parameters.AddWithValue($"@p{i}", ConvertPgValueToSqlite(value));
            }

            await insertCmd.ExecuteNonQueryAsync();
            rowCount++;
        }

        await transaction.CommitAsync();

        if (rowCount > 0)
            Log.Information("Migrated {Count} rows from '{PgTable}' to '{SqliteTable}'", rowCount, pgTable, sqliteTable);
    }

    private static async Task<List<string>> GetSqliteColumnsAsync(SqliteConnection conn, string tableName)
    {
        var columns = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1)); // column name is at index 1
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

    private static object ConvertPgValueToSqlite(object value)
    {
        return value switch
        {
            DBNull => DBNull.Value,
            // PG numeric(20,0) for ulong -> SQLite INTEGER (bit-preserving for values > long.MaxValue)
            decimal d => unchecked((long)(ulong)d),
            // PG interval -> SQLite TEXT (TimeSpan.ToString())
            TimeSpan ts => ts.ToString(),
            // PG timestamp -> SQLite TEXT (ISO 8601)
            DateTime dt => dt.ToString("o"),
            // PG boolean -> SQLite INTEGER
            bool b => b ? 1L : 0L,
            // PG real/float4 -> keep as double/float
            float f => (double)f,
            _ => value
        };
    }

    private static async Task FixAutoIncrementSequencesAsync(SqliteConnection conn)
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
            // sqlite_sequence doesn't exist if no AUTOINCREMENT tables have been inserted into
            return;
        }

        foreach (var table in tables)
        {
            await using var updateCmd = conn.CreateCommand();

            // find the PK column name
            await using var pkCmd = conn.CreateCommand();
            pkCmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            string? pkCol = null;
            await using (var pkReader = await pkCmd.ExecuteReaderAsync())
            {
                while (await pkReader.ReadAsync())
                {
                    if (pkReader.GetInt32(5) == 1) // pk flag
                    {
                        pkCol = pkReader.GetString(1);
                        break;
                    }
                }
            }

            if (pkCol is null)
                continue;

            updateCmd.CommandText =
                $"UPDATE sqlite_sequence SET seq = (SELECT MAX(\"{pkCol}\") FROM \"{table}\") WHERE name = @name";
            updateCmd.Parameters.AddWithValue("@name", table);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }
}
