using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db;

internal static class ShardIndexReconciler
{
    private const int SHARD_DIVISOR = 4194304;

    public static async Task RunAsync(NadekoContext ctx, int totalShards)
    {
        var tables = DiscoverShardTables(ctx);
        var conn = ctx.Database.GetDbConnection();

        var existing = await GetExistingShardIndexesAsync(conn);
        var expected = BuildExpectedNames(tables, totalShards);

        if (existing.SetEquals(expected))
            return;

        Log.Information("Rebuilding shard expression indexes for {TotalShards} shards...", totalShards);

        await DropIndexesAsync(conn, existing);
        await CreateIndexesAsync(conn, tables, totalShards);

        Log.Information("Shard expression indexes rebuilt for {TotalShards} shards ({Count} tables)",
            totalShards,
            tables.Count);
    }

    private static List<(string Table, string Column)> DiscoverShardTables(NadekoContext ctx)
    {
        var result = new List<(string Table, string Column)>();

        foreach (var type in typeof(NadekoContext).Assembly.GetTypes())
        {
            if (type.GetCustomAttribute<ShardFilteredAttribute>() is null)
                continue;

            var entityType = ctx.Model.FindEntityType(type);
            if (entityType is null)
                continue;

            var tableName = entityType.GetTableName();
            var columnName = entityType.FindProperty("GuildId")?.GetColumnName();

            if (tableName is null || columnName is null)
                continue;

            result.Add((tableName, columnName));
        }

        return result;
    }

    private static async Task<HashSet<string>> GetExistingShardIndexesAsync(DbConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'IX_%_OnShard_%';";

        var existing = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            existing.Add(reader.GetString(0));

        return existing;
    }

    private static HashSet<string> BuildExpectedNames(
        IReadOnlyList<(string Table, string Column)> tables,
        int totalShards)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (table, _) in tables)
            expected.Add(IndexName(table, totalShards));
        return expected;
    }

    private static async Task DropIndexesAsync(DbConnection conn, IEnumerable<string> names)
    {
        foreach (var idx in names)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP INDEX IF EXISTS \"{idx}\";";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateIndexesAsync(
        DbConnection conn,
        IReadOnlyList<(string Table, string Column)> tables,
        int totalShards)
    {
        foreach (var (table, column) in tables)
        {
            var indexName = IndexName(table, totalShards);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS \"{indexName}\" ON \"{table}\"(({column} / {SHARD_DIVISOR} % {totalShards}));";

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create shard index {IndexName}", indexName);
            }
        }
    }

    private static string IndexName(string table, int totalShards)
        => $"IX_{table}_OnShard_{totalShards}";
}
