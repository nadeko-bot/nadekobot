#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Db;
using NadekoBot.Db.ServerData;
using NadekoBot.Hosted;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ServerDataTests
{
    private const ulong GUILD = 101;
    private const ulong OTHER_GUILD = 202;

    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-serverdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
        => Directory.Delete(_dir, recursive: true);

    // A new table with server rows must be exported or excluded on purpose, and each id link must be remapped.
    [Test]
    public void Registry_CoversEveryServerTableAndEveryLinkBetweenExportedTables()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var ctx = new NadekoContext(conn);

        var tables = new HashSet<string>();
        foreach (var entity in ctx.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is not { } table)
                continue;

            tables.Add(table);
            var exported = ServerDataTables.ByName.TryGetValue(table, out var registered);
            if (entity.FindProperty(ServerDataTables.GUILD_COLUMN) is not null)
                Assert.That(exported || ServerDataTables.Excluded.Contains(table), Is.True, table);

            foreach (var fk in entity.GetForeignKeys())
            {
                var parent = fk.PrincipalEntityType.GetTableName()!;
                if (!ServerDataTables.ByName.ContainsKey(parent))
                    continue;

                Assert.That(exported || ServerDataTables.Excluded.Contains(table), Is.True, $"{table} -> {parent}");
                if (!exported)
                    continue;

                var column = fk.Properties[0].GetColumnName();
                var declared = registered!.Owner == new ParentRef(column, parent)
                               || (registered.Refs ?? []).Contains(new ParentRef(column, parent));
                Assert.That(declared, Is.True, $"{table}.{column} -> {parent}");
            }
        }

        var seen = new HashSet<string>();
        foreach (var table in ServerDataTables.All)
        {
            Assert.That(tables, Does.Contain(table.Name));
            Assert.That(table.HasGuildId || table.Owner is not null, Is.True, table.Name);
            foreach (var link in (table.Refs ?? []).Append(table.Owner ?? default).Where(static r => r.Parent is not null))
                Assert.That(seen, Does.Contain(link.Parent), $"{link.Parent} must come before {table.Name}");

            seen.Add(table.Name);
        }

        Assert.That(ServerDataTables.Excluded.Where(tables.Contains), Is.EquivalentTo(ServerDataTables.Excluded));
    }

    [Test]
    public void Build_KeepsOnlyTheGuildsRowsAndListsIndexesInsteadOfBuildingThem()
    {
        var live = NewDb();
        SeedInternal(live, GUILD, "src");
        SeedInternal(live, OTHER_GUILD, "other");

        var export = Path.Combine(_dir, "export.db");
        var stats = ServerDataExporter.Build(live, GUILD, export);

        Assert.That(stats.Schema, Is.EqualTo(LatestSchema()));
        Assert.That(stats.XpRows, Is.EqualTo(2));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM GuildConfigs WHERE GuildId <> 101"), Is.EqualTo(0));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM Permissions"), Is.EqualTo(2));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM Expressions"), Is.EqualTo(1));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM ButtonRole"), Is.EqualTo(0));
        Assert.That(Text(export, "SELECT group_concat(Message) FROM Reminders"), Is.EqualTo("src-public"));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM Repeaters WHERE LastMessageId IS NULL"), Is.EqualTo(1));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM __EFMigrationsHistory"),
            Is.EqualTo(Scalar(live, "SELECT COUNT(*) FROM __EFMigrationsHistory")));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL"), Is.EqualTo(0));
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM __ServerExportIndex WHERE Name = 'IX_Expressions_GuildId'"), Is.EqualTo(1));

        ServerDataExporter.DropXp(export);
        Assert.That(Scalar(export, "SELECT COUNT(*) FROM UserXpStats"), Is.EqualTo(0));
        Assert.That(Scalar(live, "SELECT COUNT(*) FROM UserXpStats"), Is.EqualTo(4));
    }

    [Test]
    public void ManifestAndIndexParsing_AcceptOnlyTheirOwnShape()
    {
        var manifest = new ServerExportManifest(GUILD, new string('n', 300), new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc),
            "7.6.3", "20261014181343_init", XpIncluded: false);
        var ms = new MemoryStream();
        manifest.WriteTo(ms);

        var parsed = ServerExportManifest.TryParse(ms.ToArray());
        Assert.That(parsed, Is.EqualTo(manifest with { GuildName = new string('n', ServerExportManifest.MAX_NAME_LENGTH) }));

        var json = Encoding.UTF8.GetString(ms.ToArray());
        Assert.That(ServerExportManifest.TryParse(Encoding.UTF8.GetBytes(json.Replace("nadeko-server-export", "x"))), Is.Null);
        Assert.That(ServerExportManifest.TryParse(Encoding.UTF8.GetBytes(json.Replace("\"version\": 1", "\"version\": 2"))), Is.Null);
        Assert.That(ServerExportManifest.TryParse("not json"u8), Is.Null);

        var index = ServerDataIndex.TryParse("""CREATE UNIQUE INDEX "IX_A_B" ON "A" ("B", "C")""");
        Assert.That(index?.ToSql(), Is.EqualTo("""CREATE UNIQUE INDEX "IX_A_B" ON "A" ("B", "C")"""));
        foreach (var bad in new[]
                 {
                     """CREATE INDEX "IX" ON "A" ("B"); DROP TABLE "A" """,
                     """CREATE INDEX "IX" ON "A" ("B") WHERE 1""",
                     """CREATE INDEX "IX" ON "A" (lower("B"))""",
                     """CREATE INDEX "I""X" ON "A" ("B")"""
                 })
            Assert.That(ServerDataIndex.TryParse(bad), Is.Null, bad);
    }

    [Test]
    public async Task ExportService_ReservesOncePerCooldownAndAFailureWaitsLess()
    {
        var live = NewDb();
        SeedInternal(live, GUILD, "src");
        var svc = new ServerExportService(new FileDbService(live), new MemoryBotCache(),
            new ShardData(Substitute.For<DiscordSocketClient>(), Substitute.For<IBotCreds>()));

        Assert.That(await svc.TryReserveAsync(GUILD), Is.Null);
        Assert.That(await svc.TryReserveAsync(OTHER_GUILD), Is.Null);
        Assert.That(await svc.TryReserveAsync(GUILD),
            Is.GreaterThan(ServerExportService.EXPORT_COOLDOWN - TimeSpan.FromMinutes(1)));

        await svc.ReleaseAsync(GUILD);
        Assert.That(await svc.TryReserveAsync(GUILD), Is.Null);

        await svc.MarkFailedAsync(GUILD);
        Assert.That(await svc.TryReserveAsync(GUILD), Is.LessThanOrEqualTo(ServerExportService.FAILED_EXPORT_COOLDOWN));

        Assert.That((await svc.CreateForDmAsync(999, "none")).Status, Is.EqualTo(ServerExportStatus.NoData));

        var result = await svc.CreateForDmAsync(GUILD, "Src");
        using var file = result.File!;
        Assert.That(result.Status, Is.EqualTo(ServerExportStatus.Ok));
        Assert.That(file.XpIncluded, Is.True);
        Assert.That(file.FileName, Does.Match(@"^nadeko-server-101-\d{8}-\d{6}\.zip$"));
        using var zip = ZipFile.OpenRead(file.Path);
        Assert.That(zip.Entries.Select(static e => e.FullName),
            Is.EquivalentTo(new[] { ServerDataExporter.MANIFEST_ENTRY, ServerDataExporter.DB_ENTRY }));
    }

    [Test]
    public async Task Import_ReplacesOnlyTheGuildsRowsWithNewIdsAndMakesABackupFirst()
    {
        var zip = ExportZipInternal(GUILD, xp: true);

        var (import, live, paths) = NewTarget();
        // Rows of other guilds take the ids which the export uses, so each parent needs a new id.
        SeedInternal(live, OTHER_GUILD, "other");
        SeedInternal(live, GUILD, "old");
        Exec(live, "INSERT INTO Reminders (\"When\", ChannelId, GuildId, UserId, Message, IsPrivate, Type, DateAdded) "
                   + "VALUES ('2030-01-01', 1, 101, 9, 'old-dm', 1, 0, '2026-01-01')");

        var result = await import.ImportAsync(GUILD, zip, zip.Length, new FileDbService(live), CancellationToken.None);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Status, Is.EqualTo(StatusCodes.Status202Accepted));
        Assert.That(result.Import!.XpIncluded, Is.True);
        Assert.That(File.Exists(Path.Combine(paths.BackupDir, result.Import.Backup.FileName)), Is.True);

        Assert.That(Text(live, "SELECT Prefix FROM GuildConfigs WHERE GuildId = 101"), Is.EqualTo("src"));
        Assert.That(Text(live, "SELECT Prefix FROM GuildConfigs WHERE GuildId = 202"), Is.EqualTo("other"));
        Assert.That(Scalar(live, """
                                 SELECT COUNT(*) FROM Permissions p JOIN GuildConfigs g ON g.Id = p.GuildConfigId
                                 WHERE g.GuildId = 101 AND p.GuildId = 101 AND p.SecondaryTargetName LIKE 'src%'
                                 """), Is.EqualTo(2));
        Assert.That(Scalar(live, "SELECT COUNT(*) FROM Permissions WHERE GuildId = 202 AND SecondaryTargetName LIKE 'other%'"),
            Is.EqualTo(2));
        Assert.That(Text(live, "SELECT group_concat(Message, ',') FROM (SELECT Message FROM Reminders WHERE GuildId = 101 ORDER BY Message)"),
            Is.EqualTo("old-dm,old-private,old-timely,src-public"));
        Assert.That(Text(live, "SELECT group_concat(Trigger) FROM Expressions WHERE GuildId = 101"), Is.EqualTo("src"));
        Assert.That(Scalar(live, "SELECT SUM(Xp) FROM UserXpStats WHERE GuildId = 101"), Is.EqualTo(30));
        Assert.That(Scalar(live, "SELECT COUNT(*) FROM ButtonRole WHERE GuildId = 101"), Is.EqualTo(1));
        Assert.That(Scalar(live, "PRAGMA foreign_key_check"), Is.Null);
        Assert.That(Directory.Exists(paths.ServerImportDir), Is.False);

        // On success the lock stays until the restart.
        Assert.That((await import.ImportAsync(GUILD, zip, zip.Length, new FileDbService(live), CancellationToken.None)).Error,
            Is.EqualTo(ApiErrors.SERVER_IMPORT_IN_PROGRESS));
    }

    [Test]
    public async Task Import_MigratesAnOlderExportAndKeepsTheLiveXpWhenTheExportHasNone()
    {
        var zip = ExportZipInternal(GUILD, xp: false, stagedEdit: static db =>
        {
            // The shape of an export from a bot before the guild id indexes.
            Exec(db, """
                     DELETE FROM __ServerExportIndex WHERE Name IN ('IX_Expressions_GuildId', 'IX_Repeaters_GuildId', 'IX_ImageOnlyChannels_GuildId');
                     DELETE FROM __EFMigrationsHistory;
                     INSERT INTO __EFMigrationsHistory VALUES ('20261009084103_reminder-user-guild-index', '9.0.1');
                     """);
        });

        var (import, live, _) = NewTarget();
        SeedInternal(live, GUILD, "old");
        Exec(live, "UPDATE UserXpStats SET Xp = Xp * 100");

        var result = await import.ImportAsync(GUILD, zip, zip.Length, new FileDbService(live), CancellationToken.None);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Import!.XpIncluded, Is.False);
        Assert.That(Text(live, "SELECT Prefix FROM GuildConfigs WHERE GuildId = 101"), Is.EqualTo("src"));
        Assert.That(Scalar(live, "SELECT SUM(Xp) FROM UserXpStats WHERE GuildId = 101"), Is.EqualTo(3000));
    }

    [Test]
    public async Task Import_RefusesBadExportsAndLeavesTheDatabaseAsItWas()
    {
        var (import, live, paths) = NewTarget();
        SeedInternal(live, GUILD, "old");
        var db = new FileDbService(live);

        async Task<string?> ImportInternal(MemoryStream body, ulong guildId = GUILD)
            => (await import.ImportAsync(guildId, body, body.Length, db, CancellationToken.None)).Error;

        var good = ExportZipInternal(GUILD, xp: true);
        Assert.That(await ImportInternal(good, OTHER_GUILD), Is.EqualTo(ApiErrors.GUILD_MISMATCH));
        Assert.That(await ImportInternal(new MemoryStream("nope"u8.ToArray())), Is.EqualTo(ApiErrors.BAD_EXPORT));
        Assert.That(await ImportInternal(AddEntryInternal(good, "extra.txt")), Is.EqualTo(ApiErrors.BAD_EXPORT));

        string[] hostile =
        [
            "CREATE TRIGGER t AFTER INSERT ON Quotes BEGIN DELETE FROM Quotes; END",
            "CREATE VIEW v AS SELECT 1",
            "INSERT INTO __ServerExportIndex VALUES ('x', 'Quotes', 'CREATE INDEX \"x\" ON \"Quotes\" (\"Text\"); DROP TABLE Quotes')",
            "INSERT INTO __ServerExportIndex VALUES ('y', 'sqlite_master', 'CREATE INDEX \"y\" ON \"sqlite_master\" (\"name\")')"
        ];
        foreach (var sql in hostile)
        {
            var zip = ExportZipInternal(GUILD, xp: true, stagedEdit: d => Exec(d, sql));
            Assert.That(await ImportInternal(zip), Is.EqualTo(ApiErrors.BAD_EXPORT), sql);
        }

        var tooNew = ExportZipInternal(GUILD, xp: true,
            stagedEdit: static d => Exec(d, "INSERT INTO __EFMigrationsHistory VALUES ('99991231000000_future', '9.0.1')"));
        Assert.That(await ImportInternal(tooNew), Is.EqualTo(ApiErrors.EXPORT_TOO_NEW));

        var extraColumn = ExportZipInternal(GUILD, xp: true,
            stagedEdit: static d => Exec(d, "ALTER TABLE Quotes ADD COLUMN Extra TEXT"));
        Assert.That(await ImportInternal(extraColumn), Is.EqualTo(ApiErrors.SCHEMA_MISMATCH));

        // Without its unique index, the export can hold two xp rows for one member.
        var duplicate = ExportZipInternal(GUILD, xp: true, stagedEdit: static d => Exec(d, """
            DELETE FROM __ServerExportIndex WHERE TableName = 'UserXpStats';
            INSERT INTO UserXpStats (UserId, GuildId, Xp, DateAdded) SELECT UserId, GuildId, 1, DateAdded FROM UserXpStats;
            """));
        Assert.That(await ImportInternal(duplicate), Is.EqualTo(ApiErrors.BAD_EXPORT));

        Assert.That(Text(live, "SELECT Prefix FROM GuildConfigs WHERE GuildId = 101"), Is.EqualTo("old"));
        Assert.That(Scalar(live, "SELECT COUNT(*) FROM Quotes WHERE GuildId = 101"), Is.EqualTo(1));
        Assert.That(Directory.Exists(paths.ServerImportDir), Is.False);
    }

    private (ServerImportService Import, string Live, HostPaths Paths) NewTarget()
    {
        var paths = new HostPaths(Path.Combine(_dir, "target", "data"));
        Directory.CreateDirectory(paths.DataDir);
        var live = NewDb(Path.Combine(paths.DataDir, "NadekoBot.db"));
        var time = TimeProvider.System;
        var target = new RestoreTarget(live);
        var backups = new BackupService(paths, new ApiSettings(), target, new BootGuard(paths, time), time);
        return (new ServerImportService(paths, target, backups), live, paths);
    }

    // An export of the guild from a separate source bot, with an optional change to the staged file before zipping.
    private MemoryStream ExportZipInternal(ulong guildId, bool xp, Action<string>? stagedEdit = null)
    {
        var source = NewDb();
        SeedInternal(source, guildId, "src");
        SeedInternal(source, OTHER_GUILD, "srcother");

        var id = Guid.NewGuid().ToString("N");
        var exportDb = Path.Combine(_dir, id + ".db");
        var stats = ServerDataExporter.Build(source, guildId, exportDb);
        if (!xp)
            ServerDataExporter.DropXp(exportDb);
        stagedEdit?.Invoke(exportDb);

        var zipPath = Path.Combine(_dir, id + ".zip");
        ServerDataExporter.WriteZip(exportDb,
            new ServerExportManifest(guildId, "Src", DateTime.UtcNow, "7.6.3", stats.Schema, xp), zipPath);
        return new MemoryStream(File.ReadAllBytes(zipPath));
    }

    private static MemoryStream AddEntryInternal(MemoryStream zip, string name)
    {
        var copy = new MemoryStream();
        zip.Position = 0;
        zip.CopyTo(copy);
        zip.Position = 0;
        using (var archive = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
            archive.CreateEntry(name);

        copy.Position = 0;
        return copy;
    }

    // Parents with children, one excluded row, and reminders of each kind.
    private static void SeedInternal(string path, ulong guildId, string tag)
    {
        var g = guildId.ToString();
        Exec(path, $"""
                    INSERT INTO GuildConfigs (GuildId, Prefix, DeleteMessageOnCommand, VerbosePermissions, CleverbotEnabled,
                        WarningsInitialized, VerboseErrors, NotifyStreamOffline, DeleteStreamOnlineMessage, WarnExpireHours,
                        WarnExpireAction, DisableGlobalExpressions, ExpressionOverrideEnabled, StickyRoles)
                    VALUES ({g}, '{tag}', 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0);
                    INSERT INTO Permissions (GuildId, "Index", PrimaryTarget, PrimaryTargetId, SecondaryTarget, SecondaryTargetName,
                        IsCustomCommand, State, GuildConfigId)
                    SELECT {g}, n, 0, 0, 0, '{tag}' || n, 0, 1, Id FROM GuildConfigs, (SELECT 0 AS n UNION SELECT 1)
                    WHERE GuildId = {g};
                    INSERT INTO Expressions (GuildId, Trigger, Response, AutoDeleteTrigger, DmResponse, ContainsAnywhere, AllowTarget)
                    VALUES ({g}, '{tag}', 'r', 0, 0, 0, 0);
                    INSERT INTO Quotes (GuildId, Keyword, AuthorName, AuthorId, Text) VALUES ({g}, 'K', 'a', 1, '{tag}');
                    INSERT INTO Reminders ("When", ChannelId, GuildId, UserId, Message, IsPrivate, Type, DateAdded) VALUES
                        ('2030-01-01', 1, {g}, 7, '{tag}-public', 0, 0, '2026-01-01'),
                        ('2030-01-01', 1, {g}, 7, '{tag}-private', 1, 0, '2026-01-01'),
                        ('2030-01-01', 1, {g}, 7, '{tag}-timely', 0, 1, '2026-01-01');
                    INSERT INTO Repeaters (GuildId, ChannelId, LastMessageId, Message, NoRedundant, DateAdded)
                    VALUES ({g}, 1, 555, '{tag}', 0, '2026-01-01');
                    INSERT INTO UserXpStats (UserId, GuildId, Xp, DateAdded) VALUES (1, {g}, 10, '2026-01-01'), (2, {g}, 20, '2026-01-01');
                    INSERT INTO ButtonRole (ButtonId, GuildId, ChannelId, MessageId, Position, RoleId, Emote, Label, Exclusive)
                    VALUES ('{tag}', {g}, 1, 1, 0, {g}, '', '', 0);
                    """);
    }

    private string NewDb(string? path = null)
    {
        path ??= Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        using var conn = ServerDataExporter.OpenFile(path);
        using var ctx = new NadekoContext(conn);
        ctx.Database.Migrate();
        return path;
    }

    private static string LatestSchema()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        using var ctx = new NadekoContext(conn);
        return DbMigrator.GetLatestKnown(ctx);
    }

    private static void Exec(string path, string sql)
    {
        using var conn = ServerDataExporter.OpenFile(path, SqliteOpenMode.ReadWrite);
        ServerDataExporter.Execute(conn, sql);
    }

    private static object? Query(string path, string sql)
    {
        using var conn = ServerDataExporter.OpenFile(path, SqliteOpenMode.ReadOnly);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static long? Scalar(string path, string sql)
        => Query(path, sql) is { } value and not DBNull ? Convert.ToInt64(value) : null;

    private static string? Text(string path, string sql)
        => Query(path, sql) as string;

    private sealed class FileDbService(string path) : DbService
    {
        public override NadekoContext GetDbContext()
            => new($"Data Source={path};Pooling=False");

        public override Task SetupAsync()
            => Task.CompletedTask;

        public override string ConnectionString
            => $"Data Source={path};Pooling=False";
    }
}
