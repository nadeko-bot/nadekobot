#nullable disable
using System.Data.Common;

namespace NadekoBot.Modules.Owner.Services;

public class DangerousCommandsService : INService
{
    public const int MAX_SELECT_ROWS = 1000;
    private const int MAX_SELECT_CHARS = 8 * 1024 * 1024;
    private const int MAX_CELL_CHARS = 1000;

    private readonly DbService _db;

    public DangerousCommandsService(DbService db)
        => _db = db;

    public async Task<int> ExecuteSql(string sql)
    {
        await using var conn = OwnerSqlConnection.Open(_db.ConnectionString);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = OwnerSqlConnection.COMMAND_TIMEOUT_SECONDS;
        return await cmd.ExecuteNonQueryAsync();
    }

    public SelectResult SelectSql(string sql)
    {
        var result = new SelectResult
        {
            ColumnNames = new(),
            Results = new()
        };

        using var conn = OwnerSqlConnection.Open(_db.ConnectionString);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = OwnerSqlConnection.COMMAND_TIMEOUT_SECONDS;
        using var reader = cmd.ExecuteReader();
        if (reader.HasRows)
        {
            for (var i = 0; i < reader.FieldCount; i++)
                result.ColumnNames.Add(reader.GetName(i));

            long totalChars = 0;
            while (reader.Read())
            {
                if (result.Results.Count >= MAX_SELECT_ROWS || totalChars >= MAX_SELECT_CHARS)
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

        var value = reader.GetValue(column);
        var text = value is byte[] blob ? $"<{blob.Length} bytes>" : value.ToString() ?? string.Empty;
        if (text.Length <= MAX_CELL_CHARS)
            return text;

        truncated = true;
        return text[..MAX_CELL_CHARS];
    }

    public class SelectResult
    {
        public List<string> ColumnNames { get; set; }
        public List<string[]> Results { get; set; }
        public bool Truncated { get; set; }
    }
}