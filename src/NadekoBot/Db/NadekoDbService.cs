using LinqToDB.Common;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace NadekoBot.Db;

public sealed class NadekoDbService : DbService
{
    private readonly IBotCredsProvider _creds;

    // these are props because creds can change at runtime
    private string DbType
        => _creds.GetCreds().Db.Type.ToLowerInvariant().Trim();

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
        await using var context = CreateRawDbContext(DbType, ConnString);

        await RunMigration(context);

        // make sure sqlite db is in wal journal mode
        if (context is SqliteContext)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
        }
    }

    public override NadekoContext CreateRawDbContext(string dbType, string connString)
    {
        switch (dbType)
        {
            case "postgresql":
            case "postgres":
            case "pgsql":
                return new PostgreSqlContext(connString);
            case "sqlite":
                return new SqliteContext(connString);
            default:
                throw new NotSupportedException($"The database provide type of '{dbType}' is not supported.");
        }
    }

    private NadekoContext GetDbContextInternal()
    {
        var dbType = DbType;
        var connString = ConnString;

        var context = CreateRawDbContext(dbType, connString);
        if (context is SqliteContext)
        {
            var conn = context.Database.GetDbConnection();
            conn.Open();
            using var com = conn.CreateCommand();
            com.CommandText = "PRAGMA synchronous=OFF";
            com.ExecuteNonQuery();
        }

        return context;
    }

    public override NadekoContext GetDbContext()
        => GetDbContextInternal();


    private static async Task RunMigration(DbContext ctx)
    {
        // if database doesn't exist, run the baseline migration
        if (!await ctx.Database.CanConnectAsync())
        {
            Log.Information("Database does not exist. Creating a new database...");
            await ctx.Database.MigrateAsync();
            return;
        }

        // get the latest applied migration

        var applied = await ctx.Database.GetAppliedMigrationsAsync();

        // get all .sql file names from the migrations folder
        var available = Directory.GetFiles("Migrations/" + GetMigrationDirectory(ctx.Database), "*_*.sql")
            .Select(x => Path.GetFileNameWithoutExtension(x))
            .OrderBy(x => x);

        var lastApplied = applied.Last();
        Log.Information("Last applied migration: {LastApplied}", lastApplied);

        // apply all migrations with names greater than the last applied
        foreach (var runnable in available)
        {
            if (string.Compare(lastApplied, runnable, StringComparison.Ordinal) < 0)
            {
                Log.Warning("Migration {MigrationName} has not been applied yet", runnable);

                var query = await File.ReadAllTextAsync(GetMigrationPath(ctx.Database, runnable));
                await ctx.Database.ExecuteSqlRawAsync(query);
            }
        }

        // run all migrations that have not been applied yet
    }

    private static string GetMigrationPath(DatabaseFacade ctxDatabase, string runnable)
    {
        return $"Migrations/{GetMigrationDirectory(ctxDatabase)}/{runnable}.sql";
    }

    private static string GetMigrationDirectory(DatabaseFacade ctxDatabase)
    {
        if (ctxDatabase.IsSqlite())
            return "Sqlite";

        if (ctxDatabase.IsNpgsql())
            return "PostgreSql";

        throw new NotSupportedException("This database type is not supported.");
    }
}