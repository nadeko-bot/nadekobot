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
        if (!await TryMigrateFromPostgresAsync())
            return;

        await using var ctx = new NadekoContext(ConnString);
        await DbMigrator.RunAsync(ctx);

        var conn = ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await DbPragmas.ApplySetupAsync(conn);
            await ShardIndexReconciler.RunAsync(ctx, _creds.GetCreds().TotalShards);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public override NadekoContext GetDbContext()
    {
        var ctx = new NadekoContext(ConnString);
        var conn = ctx.Database.GetDbConnection();
        conn.Open();
        DbPragmas.ApplyRuntime(conn);
        return ctx;
    }

    private async Task<bool> TryMigrateFromPostgresAsync()
    {
        var dbType = _creds.GetCreds().Db.Type.ToLowerInvariant().Trim();

        if (dbType is not ("postgresql" or "postgres" or "pgsql"))
            return true;

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
            return false;
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

        return true;
    }
}
