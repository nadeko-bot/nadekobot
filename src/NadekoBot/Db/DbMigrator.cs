using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db;

internal static class DbMigrator
{
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

        await ApplyCustomSqlMigrationsAsync(ctx, applied.Last());
    }

    private static async Task ApplyCustomSqlMigrationsAsync(NadekoContext ctx, string lastApplied)
    {
        var available = Directory.GetFiles("Migrations", "*_*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static x => x is { Length: > 0 } && char.IsAsciiDigit(x[0]))
            .OrderBy(static x => x, StringComparer.Ordinal);

        foreach (var runnable in available)
        {
            if (string.Compare(lastApplied, runnable, StringComparison.Ordinal) >= 0)
                continue;

            Log.Warning("Applying migration {MigrationName}", runnable);

            var query = await File.ReadAllTextAsync($"Migrations/{runnable}.sql");
            await ctx.Database.ExecuteSqlRawAsync(query);
        }
    }
}
