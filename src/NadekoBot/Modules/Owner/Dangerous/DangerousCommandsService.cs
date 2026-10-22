#nullable disable
using System.Data.Common;

namespace NadekoBot.Modules.Owner.Services;

public class DangerousCommandsService : INService
{
    public const int MAX_SELECT_ROWS = 1000;
    public const int MAX_SELECT_CHARS = 8 * 1024 * 1024;
    private const int MAX_CELL_CHARS = 1000;

    // The owner of a managed bot does not control the host, so the bot limits the time and the growth of a write.
    public static readonly TimeSpan ManagedExecTimeLimit = TimeSpan.FromSeconds(OwnerSqlConnection.COMMAND_TIMEOUT_SECONDS);

    private readonly DbService _db;
    private readonly BotPolicy _policy;

    // Sorts and recursive queries can write gigabytes of temp files, so each shard runs one owner read at a time.
    private readonly SemaphoreSlim _selectGate = new(1, 1);

    public DangerousCommandsService(DbService db, BotPolicy policy)
    {
        _db = db;
        _policy = policy;
    }

    public Task<int> ExecuteSql(string sql)
        => ExecuteSql(sql, _policy.Managed ? ManagedExecTimeLimit : null);

    // Throws SqliteException with SQLITE_INTERRUPT at the time limit.
    public async Task<int> ExecuteSql(string sql, TimeSpan? timeLimit)
    {
        if (sql.Contains('\0'))
            throw new ArgumentException("The query has a NUL character.", nameof(sql));

        await using var conn = OwnerSqlConnection.Open(_db.ConnectionString, timeLimit);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = OwnerSqlConnection.COMMAND_TIMEOUT_SECONDS;
        return await cmd.ExecuteNonQueryAsync();
    }

    public static readonly TimeSpan SelectTimeLimit = TimeSpan.FromSeconds(15);

    public SelectResult SelectSql(string sql, int maxChars = MAX_SELECT_CHARS)
        => SelectSql(sql, SelectTimeLimit, maxChars);

    // Throws SqliteException when SQLite refuses the query, or with SQLITE_INTERRUPT at the time limit.
    // Throws NotOneStatementException when the text has no statement or more than one.
    // Throws QueryBusyException while another query runs.
    public SelectResult SelectSql(string sql, TimeSpan timeLimit, int maxChars = MAX_SELECT_CHARS)
    {
        if (!_selectGate.Wait(0))
            throw new QueryBusyException();

        try
        {
            return SelectSqlInternal(sql, timeLimit, maxChars);
        }
        finally
        {
            _selectGate.Release();
        }
    }

    private SelectResult SelectSqlInternal(string sql, TimeSpan timeLimit, int maxChars)
    {
        var result = new SelectResult
        {
            ColumnNames = new(),
            Results = new()
        };

        using var conn = OwnerSqlConnection.OpenReadOnly(_db.ConnectionString, timeLimit);
        if (!OwnerSqlConnection.IsSingleStatement(conn, sql))
            throw new NotOneStatementException();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = (int)timeLimit.TotalSeconds + 1;
        using var reader = cmd.ExecuteReader();
        if (reader.FieldCount > 0)
        {
            for (var i = 0; i < reader.FieldCount; i++)
                result.ColumnNames.Add(reader.GetName(i));

            long totalChars = 0;
            while (reader.Read())
            {
                if (result.Results.Count >= MAX_SELECT_ROWS || totalChars >= maxChars)
                {
                    result.Truncated = true;
                    break;
                }

                var row = new string[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = ReadCellInternal(reader, i, out var cut);
                    result.Truncated |= cut;
                    totalChars += row[i].Length;
                }

                result.Results.Add(row);
            }
        }

        return result;
    }

    private static string ReadCellInternal(DbDataReader reader, int column, out bool truncated)
    {
        truncated = false;
        if (reader.IsDBNull(column))
            return string.Empty;

        if (reader.GetFieldType(column) == typeof(byte[]))
            return $"<{reader.GetBytes(column, 0, null, 0, 0)} bytes>";

        var text = reader.GetValue(column).ToString() ?? string.Empty;
        if (text.Length <= MAX_CELL_CHARS)
            return text;

        truncated = true;
        return text[..MAX_CELL_CHARS];
    }

    public sealed class NotOneStatementException() : Exception("The query must have exactly one statement.");

    public sealed class QueryBusyException() : Exception("Another owner query is running.");

    public class SelectResult
    {
        public List<string> ColumnNames { get; set; }
        public List<string[]> Results { get; set; }
        public bool Truncated { get; set; }
    }
}