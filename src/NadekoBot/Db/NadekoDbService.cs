using LinqToDB.Common;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Db;

public sealed class NadekoDbService : DbService
{
    private readonly IBotCredsProvider _creds;

    private string ConnString
        => _creds.GetCreds().Db.ConnectionString;

    public NadekoDbService(IBotCredsProvider creds)
    {
        LinqToDBForEFTools.Initialize();
        Configuration.Linq.DisableQueryCache = true;

        _creds = creds;
    }

    public override async Task SetupAsync()
    {
        var dbType = _creds.GetCreds().Db.Type.ToLowerInvariant().Trim();

        if (dbType is "postgresql" or "postgres" or "pgsql")
        {
            Log.Warning("PostgreSQL support has been removed. Migrating your data to SQLite automatically...");

            try
            {
                await PostgresMigrator.MigrateAsync(ConnString);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PostgreSQL to SQLite migration failed. "
                              + "Your PostgreSQL database has NOT been modified. "
                              + "Please report this issue");
                Helpers.ReadErrorAndExit(110);
                return;
            }

            _creds.ModifyCredsFile(c =>
            {
                c.Db.Type = "sqlite";
                c.Db.ConnectionString = "Data Source=data/NadekoBot.db";
            });
            _creds.Reload();

            Log.Warning("Migration complete. creds.yml has been updated to use SQLite. "
                        + "Your data is now in data/NadekoBot.db. "
                        + "You may uninstall PostgreSQL if you no longer need it");
        }

        await using var context = new NadekoContext(ConnString);

        await RunMigration(context);

        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;");
    }

    private NadekoContext GetDbContextInternal()
    {
        var context = new NadekoContext(ConnString);
        var conn = context.Database.GetDbConnection();
        conn.Open();
        using var com = conn.CreateCommand();
        com.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";
        com.ExecuteNonQuery();

        return context;
    }

    public override NadekoContext GetDbContext()
        => GetDbContextInternal();

    private static async Task RunMigration(DbContext ctx)
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

        var available = Directory.GetFiles("Migrations", "*_*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static x => x.Length > 0 && char.IsAsciiDigit(x[0]))
            .OrderBy(static x => x);

        var lastApplied = applied.Last();

        foreach (var runnable in available)
        {
            if (string.Compare(lastApplied, runnable, StringComparison.Ordinal) < 0)
            {
                Log.Warning("Applying migration {MigrationName}", runnable);

                var query = await File.ReadAllTextAsync($"Migrations/{runnable}.sql");
                await ctx.Database.ExecuteSqlRawAsync(query);
            }
        }
    }
}