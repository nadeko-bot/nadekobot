using System.Text.RegularExpressions;

namespace NadekoBot.Db.ServerData;

// An index of the live database, which a server export lists instead of building it.
public sealed partial record ServerDataIndex(string Name, string Table, bool Unique, string[] Columns)
{
    private const int UNIQUE_GROUP = 1;
    private const int NAME_GROUP = 2;
    private const int TABLE_GROUP = 3;
    private const int COLUMNS_GROUP = 4;

    // The shape of each index which a migration creates. Expression indexes (shard indexes) do not match.
    [GeneratedRegex("""
                    ^CREATE (UNIQUE )?INDEX "([A-Za-z0-9_]+)" ON "([A-Za-z0-9_]+)" \(("[A-Za-z0-9_]+"(?:, "[A-Za-z0-9_]+")*)\)\z
                    """,
        RegexOptions.CultureInvariant)]
    private static partial Regex IndexSqlRegex();

    public static ServerDataIndex? TryParse(string sql)
    {
        var match = IndexSqlRegex().Match(sql);
        if (!match.Success)
            return null;

        var columnList = match.Groups[COLUMNS_GROUP].Value;
        var columns = columnList.Split(", ");
        for (var i = 0; i < columns.Length; i++)
            columns[i] = columns[i][1..^1];

        return new(match.Groups[NAME_GROUP].Value,
            match.Groups[TABLE_GROUP].Value,
            match.Groups[UNIQUE_GROUP].Success,
            columns);
    }

    // Built from the checked names only, so text from an export file never runs as sql.
    public string ToSql()
        => $"CREATE {(Unique ? "UNIQUE " : string.Empty)}INDEX \"{Name}\" ON \"{Table}\" (\"{string.Join("\", \"", Columns)}\")";
}
