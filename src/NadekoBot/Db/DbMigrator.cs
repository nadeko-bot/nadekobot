using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db;

public static class DbMigrator
{
    public const string MIGRATIONS_DIR = "Migrations";

    public static async Task RunAsync(NadekoContext ctx)
    {
        if (!await ctx.Database.CanConnectAsync())
        {
            Log.Information("Database does not exist. Creating a new database...");
            await ctx.Database.MigrateAsync();
            return;
        }

        var applied = await ctx.Database.GetAppliedMigrationsAsync();

        if (!applied.Any())
        {
            Log.Information("No migrations applied. Running baseline migration...");
            await ctx.Database.MigrateAsync();
            return;
        }

        await ApplySqlMigrationsAsync(ctx, applied.Last());
    }

    // Each sql file inserts its own history row, so the order of the file names is the order of the schema.
    public static async Task<int> ApplySqlMigrationsAsync(
        NadekoContext ctx,
        string lastApplied,
        string migrationsDir = MIGRATIONS_DIR)
    {
        var count = 0;
        foreach (var runnable in GetSqlMigrations(migrationsDir))
        {
            if (string.Compare(lastApplied, runnable, StringComparison.Ordinal) >= 0)
                continue;

            Log.Warning("Applying migration {MigrationName}", runnable);

            var query = await File.ReadAllTextAsync(Path.Combine(migrationsDir, runnable + ".sql"));
            await ctx.Database.ExecuteSqlRawAsync(query);
            count++;
        }

        return count;
    }

    // The newest schema which this build can produce: the last sql file or the baseline, whichever is later.
    public static string GetLatestKnown(NadekoContext ctx, string migrationsDir = MIGRATIONS_DIR)
    {
        var latest = ctx.Database.GetMigrations().LastOrDefault() ?? string.Empty;
        if (GetSqlMigrations(migrationsDir).LastOrDefault() is { } sql && string.CompareOrdinal(sql, latest) > 0)
            latest = sql;

        return latest;
    }

    private static IOrderedEnumerable<string> GetSqlMigrations(string migrationsDir)
        => Directory.GetFiles(migrationsDir, "*_*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static x => x is { Length: > 0 } && char.IsAsciiDigit(x[0]))
            .Select(static x => x!)
            .OrderBy(static x => x, StringComparer.Ordinal);
}
