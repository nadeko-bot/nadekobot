using DryIoc;
using Microsoft.Data.Sqlite;
using NadekoBot.Common.Configs;
using Microsoft.EntityFrameworkCore;

namespace NadekoBot.Migrate;

// Neither command logs in to Discord, so they work without a token.
public static class MigrateCommand
{
    public const int EXIT_OK = 0;
    public const int EXIT_FAILED = 1;
    public const int EXIT_TOO_NEW = 2;

    public static async Task<int> RunAsync(string? path)
    {
        LogSetup.SetupLogger("migrate", null);
        try
        {
            return await RunInternalAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The migration failed");
            return EXIT_FAILED;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            await Log.CloseAndFlushAsync();
        }
    }

    public static int RunConfigs()
    {
        LogSetup.SetupLogger("migrate", null);
        try
        {
            return RunConfigsInternal();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The config migration failed");
            return EXIT_FAILED;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int RunConfigsInternal()
    {
        if (!Directory.Exists("data"))
        {
            Log.Error("There is no data folder in {Path}", Directory.GetCurrentDirectory());
            return EXIT_FAILED;
        }

        if (File.Exists(BotCredsProvider.CREDS_FILE_NAME))
        {
            BotCredsProvider.MigrateCredentials();
            Log.Information("Checked {File}", BotCredsProvider.CREDS_FILE_NAME);
        }
        else
        {
            Log.Warning("{File} does not exist", BotCredsProvider.CREDS_FILE_NAME);
        }

        using var container = new Container();
        container.AddSingleton(new BotPolicy(false));
        container.AddSingleton<IConfigSeria, YamlSeria>();
        container.AddSingleton<IPubSub, EventPubSub>();
        container.AddConfigServices(typeof(Bot).Assembly);

        var failed = 0;
        foreach (var type in ServiceCollectionExtensions.GetConfigServiceTypes(typeof(Bot).Assembly))
        {
            try
            {
                var config = (IConfigService)container.Resolve(type);
                Log.Information("Checked the {Name} config", config.Name);
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error(ex, "Could not update the config of {Type}", type.Name);
            }
        }

        return failed == 0 ? EXIT_OK : EXIT_FAILED;
    }

    private static async Task<int> RunInternalAsync(string? path)
    {
        if (!Directory.Exists(DbMigrator.MIGRATIONS_DIR))
        {
            Log.Error("There is no {Dir} folder in {Path}", DbMigrator.MIGRATIONS_DIR, Directory.GetCurrentDirectory());
            return EXIT_FAILED;
        }

        if (path is null)
        {
            var creds = new BotCredsProvider().GetCreds();
            // The bot converts a PostgreSQL database next to its own exe, which is not this tool's folder.
            if (!creds.Db.Type.Trim().Equals("sqlite", StringComparison.InvariantCultureIgnoreCase))
            {
                Log.Error("The database type is {Type}. Start the bot once: it converts the database to SQLite", creds.Db.Type);
                return EXIT_FAILED;
            }

            return await MigrateFileInternalAsync(creds.Db.ConnectionString, creds.TotalShards);
        }

        var fullPath = Path.GetFullPath(path);
        // a typo must not create a new empty database
        if (!File.Exists(fullPath))
        {
            Log.Error("The file {Path} does not exist", fullPath);
            return EXIT_FAILED;
        }

        var connString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        return await MigrateFileInternalAsync(connString, 1);
    }

    private static async Task<int> MigrateFileInternalAsync(string connString, int totalShards)
    {
        string before;
        await using (var ctx = new NadekoContext(connString))
        {
            before = await GetLastAppliedInternalAsync(ctx);
            var latest = DbMigrator.GetLatestKnown(ctx);
            if (string.CompareOrdinal(before, latest) > 0)
            {
                Log.Error("The database is at {Before}, which is newer than this bot ({Latest})", before, latest);
                return EXIT_TOO_NEW;
            }
        }

        await NadekoDbService.MigrateSqliteAsync(connString, totalShards);

        await using (var ctx = new NadekoContext(connString))
        {
            var after = await GetLastAppliedInternalAsync(ctx);
            Log.Information("The database was at {Before} and is now at {After}",
                before.Length == 0 ? "(empty)" : before,
                after);
        }

        return EXIT_OK;
    }

    private static async Task<string> GetLastAppliedInternalAsync(NadekoContext ctx)
    {
        if (!await ctx.Database.CanConnectAsync())
            return string.Empty;

        return (await ctx.Database.GetAppliedMigrationsAsync()).LastOrDefault() ?? string.Empty;
    }
}
