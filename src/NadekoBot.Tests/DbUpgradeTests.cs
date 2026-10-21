#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NadekoBot.Db;
using NadekoBot.Migrate;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class DbUpgradeTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-dbupgrade-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task V5Database_ReachesTheSchemaOfANewDatabase()
    {
        var upgraded = NewV5Db();
        await NadekoDbService.MigrateSqliteAsync(ConnString(upgraded), 1);

        var fresh = Path.Combine(_dir, "fresh.db");
        await File.WriteAllBytesAsync(fresh, []);
        await NadekoDbService.MigrateSqliteAsync(ConnString(fresh), 1);

        var have = SchemaObjects(upgraded);
        var missing = new List<string>();
        foreach (var item in SchemaObjects(fresh))
        {
            if (!have.Contains(item))
                missing.Add(item);
        }

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public async Task V5Database_WithRowsOfADeletedServerConfig_Upgrades()
    {
        var path = NewV5Db();
        Exec(path, """
                   INSERT INTO GuildConfigs (Id, GuildId, AutoDeleteSelfAssignedRoleMessages, CleverbotEnabled,
                       DeleteMessageOnCommand, DeleteStreamOnlineMessage, DisableGlobalExpressions,
                       ExclusiveSelfAssignedRoles, FilterInvites, FilterLinks, FilterWords, NotifyStreamOffline,
                       StickyRoles, VerbosePermissions, WarnExpireAction, WarnExpireHours, WarningsInitialized)
                   VALUES (1, 10, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1);
                   INSERT INTO FilteredWord (GuildConfigId, Word) VALUES (1, 'kept'), (99, 'orphan');
                   INSERT INTO FilterChannelId (GuildConfigId, ChannelId) VALUES (99, 5);
                   """);

        await NadekoDbService.MigrateSqliteAsync(ConnString(path), 1);

        Assert.That(Scalar(path, "SELECT group_concat(Word) FROM FilteredWord"), Is.EqualTo("kept"));
        Assert.That(Scalar(path, "SELECT count(*) FROM FilterChannelId"), Is.EqualTo(0L));
    }

    [Test]
    public async Task MigrateCommand_RefusesAMissingFileAndANewerDatabase()
    {
        var missing = Path.Combine(_dir, "missing.db");
        Assert.That(await MigrateCommand.RunAsync(missing), Is.EqualTo(MigrateCommand.EXIT_FAILED));
        Assert.That(File.Exists(missing), Is.False);

        var path = NewV5Db();
        Exec(path, "INSERT INTO __EFMigrationsHistory VALUES ('99990101000000_future', '9.0.0');");
        var before = Scalar(path, "SELECT count(*) FROM sqlite_master");

        Assert.That(await MigrateCommand.RunAsync(path), Is.EqualTo(MigrateCommand.EXIT_TOO_NEW));
        Assert.That(Scalar(path, "SELECT count(*) FROM sqlite_master"), Is.EqualTo(before));
    }

    private string NewV5Db()
    {
        var path = Path.Combine(_dir, System.Guid.NewGuid().ToString("N") + ".db");
        var dump = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "db", "v5.3.9.sql");
        Exec(path, File.ReadAllText(dump));
        return path;
    }

    private static string ConnString(string path)
        => new SqliteConnectionStringBuilder { DataSource = path }.ToString();

    private static void Exec(string path, string sql)
    {
        using var conn = new SqliteConnection(ConnString(path));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql)
    {
        using var conn = new SqliteConnection(ConnString(path));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    // EF makes __EFMigrationsLock on demand, so only some databases have it.
    private static HashSet<string> SchemaObjects(string path)
    {
        using var conn = new SqliteConnection(ConnString(path));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT 'table ' || name FROM sqlite_master
                          WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsLock'
                          UNION ALL
                          SELECT 'column ' || m.name || '.' || p.name
                          FROM sqlite_master m JOIN pragma_table_info(m.name) p
                          WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%' AND m.name <> '__EFMigrationsLock'
                          UNION ALL
                          SELECT 'index ' || name FROM sqlite_master
                          WHERE type = 'index' AND name NOT LIKE 'sqlite_autoindex_%' AND name NOT LIKE '%_OnShard_%';
                          """;
        var result = new HashSet<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }
}
