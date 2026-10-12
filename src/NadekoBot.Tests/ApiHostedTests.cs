#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Common;
using NadekoBot.Common.Yml;
using NadekoBot.Hosted;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiHostedTests
{
    private const string KEY = "placeholder-key-xxxxxxxxxxxxxxxx";
    private const string BACKUP_NAME = "nadeko-backup-20260101-000000.zip";

    private string _dir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-hosted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
        => Directory.Delete(_dir, recursive: true);

    [Test]
    public void HostContext_RoutesByDiscordShardFormulaAndArgsAcceptOnlyAShardBelowTheTotal()
    {
        var host = new HostContext(HostMode.Coordinated, 2, 4);

        Assert.That(host.OwnsGuild(2UL << 22), Is.True);
        Assert.That(host.OwnsGuild(6UL << 22), Is.True);
        Assert.That(host.OwnsGuild(1UL << 22), Is.False);
        Assert.That(new HostContext(HostMode.Standalone, 0, 1).OwnsGuild(1UL << 22), Is.True);

        Assert.That(new HostContext(HostMode.Coordinated, 3, 8).ApiPort(5010), Is.EqualTo(5013));
        Assert.That(new HostContext(HostMode.Standalone, 3, 8).ApiPort(5010), Is.EqualTo(5010));

        Assert.That(HostArgs.TryParse([], out var none, out _), Is.True);
        Assert.That(none, Is.EqualTo(new HostArgs(0, null)));
        Assert.That(HostArgs.TryParse(["3", "8"], out var some, out _), Is.True);
        Assert.That(some, Is.EqualTo(new HostArgs(3, 8)));

        Assert.That(HostArgs.TryParse(["8", "8"], out _, out _), Is.False);
        Assert.That(HostArgs.TryParse(["x"], out _, out _), Is.False);
        Assert.That(HostArgs.TryParse(["run", "x"], out _, out _), Is.False);
        Assert.That(HostArgs.TryParse(["0", "2", "x"], out _, out _), Is.False);
    }

    [Test]
    public void Validate_ReportsWhyAnArchiveIsRejectedAndAcceptsOnlyPlainRelativeEntryNames()
    {
        string[] safe = ["NadekoBot.db", "data/medusae/a/b.dll", "data/"];
        string[] unsafeNames =
        [
            "data/../creds.yml", "data/./bot.yml", "data//bot.yml", "data/bot\u0001.yml",
            "/etc/passwd", "data\\x.yml", "C:/x", ""
        ];

        Assert.That(safe.Where(BackupArchive.IsSafeEntryName), Is.EqualTo(safe));
        Assert.That(unsafeNames.Where(BackupArchive.IsSafeEntryName), Is.Empty);

        var limits = BackupLimits.ForImport(1024 * 1024);
        var db = SqliteBytes("new");

        Assert.That(BackupArchive.Validate(new MemoryStream("nope"u8.ToArray()), limits), Is.EqualTo("not_a_zip"));
        Assert.That(BackupArchive.Validate(Zip(("data/bot.yml", "a"u8.ToArray())), limits), Is.EqualTo("missing_db"));
        Assert.That(BackupArchive.Validate(Zip(("NadekoBot.db", "db"u8.ToArray())), limits), Is.EqualTo("not_sqlite"));
        Assert.That(BackupArchive.Validate(Zip(("NadekoBot.db", db), ("data/../x.yml", "a"u8.ToArray())), limits),
            Is.EqualTo("unsafe_entry"));
        Assert.That(BackupArchive.Validate(Zip(("NadekoBot.db", db), ("data/big.yml", new byte[2 * 1024 * 1024])),
                BackupLimits.ForImport(128 * 1024)),
            Is.EqualTo("archive_too_large"));
        Assert.That(BackupArchive.Validate(Zip(("NadekoBot.db", db), ("data/bot.yml", "a"u8.ToArray()),
                ("data/bot.yml", "b"u8.ToArray())), limits),
            Is.EqualTo("unsafe_entry"));
        Assert.That(BackupArchive.Validate(Zip(("NadekoBot.db", db), ("data/bot.yml", "a: &x [1]\nb: *x"u8.ToArray())),
                limits),
            Is.EqualTo("unsafe_entry"));
        Assert.That(BackupArchive.Validate(Zip(("data/NadekoBot.db", db), ("data/bot.yml", "a: 'not *x'"u8.ToArray())),
                limits),
            Is.Null);

        // The end record claims more entries than allowed, so the central directory is never loaded.
        var bomb = new byte[22];
        BitConverter.TryWriteBytes(bomb.AsSpan(0), 0x06054b50u);
        BitConverter.TryWriteBytes(bomb.AsSpan(10), (ushort)(BackupLimits.MAX_ENTRIES + 1));
        Assert.That(BackupArchive.Validate(new MemoryStream(bomb), limits), Is.EqualTo("archive_too_large"));
    }

    [Test]
    public async Task BackupService_ARequestedRestoreBlocksOtherBackupWork()
    {
        var (paths, dbPath) = RestorePaths();
        WriteArchive(paths, ("NadekoBot.db", SqliteBytes("new")));
        using var db = new TestDbService();
        var time = TimeProvider.System;

        var unsupported = new BackupService(paths, new ApiSettings(), new RestoreTarget(null), new BootGuard(paths, time), time);
        Assert.That((await unsupported.CreateAsync(db)).Error, Is.EqualTo("backup_unsupported"));
        Assert.That((await unsupported.RequestRestoreAsync(BACKUP_NAME)).Error, Is.EqualTo("restore_unsupported"));

        var backups = new BackupService(paths, new ApiSettings(), new RestoreTarget(dbPath), new BootGuard(paths, time), time);
        Assert.That((await backups.RequestRestoreAsync("nadeko-backup-20990101-000000.zip")).Error, Is.EqualTo("not_found"));
        Assert.That(await backups.RequestRestoreAsync(BACKUP_NAME), Is.EqualTo(BackupResult.Done));
        Assert.That(RestoreMarker.Parse(File.ReadAllText(paths.RestoreMarker)).FileName, Is.EqualTo(BACKUP_NAME));

        Assert.That((await backups.RequestRestoreAsync(BACKUP_NAME)).Error, Is.EqualTo("restore_in_progress"));
        Assert.That((await backups.CreateAsync(db)).Error, Is.EqualTo("restore_in_progress"));
        Assert.That(backups.Delete(BACKUP_NAME).Error, Is.EqualTo("restore_in_progress"));
        Assert.That(File.Exists(Path.Combine(paths.BackupDir, BACKUP_NAME)), Is.True);
    }

    [Test]
    public void ApplyPendingRestore_CanPutTheSettingsBack()
    {
        var (paths, dbPath) = RestorePaths();
        var botYml = Path.Combine(paths.DataDir, "bot.yml");
        var newYml = Path.Combine(paths.DataDir, "games.yml");
        File.WriteAllBytes(dbPath, SqliteBytes("old"));
        File.WriteAllText(botYml, "old");
        WriteArchive(paths, ("NadekoBot.db", SqliteBytes("new")),
            ("data/bot.yml", "new"u8.ToArray()),
            ("data/games.yml", "new"u8.ToArray()));
        File.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(BACKUP_NAME).ToString());

        PendingRestore.Apply(paths, dbPath);

        Assert.That(File.ReadAllText(botYml), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(newYml), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(Path.Combine(paths.PreRestoreFiles, "bot.yml")), Is.EqualTo("old"));

        // A restore which keeps crashing after the set-aside gives up and brings back the settings it replaced.
        var exhausted = new RestoreMarker(BACKUP_NAME, true, PendingRestore.MAX_RESTORE_ATTEMPTS);
        File.WriteAllText(paths.RestoreMarker, exhausted.ToString());

        PendingRestore.Apply(paths, dbPath);

        Assert.That(ReadTag(dbPath), Is.EqualTo("old"));
        Assert.That(File.ReadAllText(botYml), Is.EqualTo("old"));
        Assert.That(File.Exists(newYml), Is.False);
    }

    [Test]
    public void ApplyPendingRestore_SwapsDbAndRestoresOnlyRootSettings()
    {
        var (paths, dbPath) = RestorePaths();
        WriteDbWithUncheckpointedWal(dbPath, "old");
        File.WriteAllText(dbPath + ".pre-restore-wal", "an earlier restore's wal");
        File.WriteAllText(paths.ApiSettings, "key");
        File.WriteAllText(paths.Creds, "creds");

        WriteArchive(paths, ("NadekoBot.db", SqliteBytes("new")),
            ("data/bot.yml", "restored"u8.ToArray()),
            ("data/api.yml", "evil"u8.ToArray()),
            ("data/creds.yml", "evil"u8.ToArray()),
            ("data/medusae/evil/evil.dll", "evil"u8.ToArray()));

        File.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(BACKUP_NAME).ToString());

        PendingRestore.Apply(paths, dbPath);

        Assert.That(ReadTag(dbPath), Is.EqualTo("new"));
        Assert.That(File.Exists(dbPath + "-wal"), Is.False);
        Assert.That(File.Exists(dbPath + ".pre-restore-wal"), Is.False);
        Assert.That(ReadTag(dbPath + ".pre-restore"), Is.EqualTo("old"));
        Assert.That(File.ReadAllText(Path.Combine(paths.DataDir, "bot.yml")), Is.EqualTo("restored"));
        Assert.That(File.ReadAllText(paths.ApiSettings), Is.EqualTo("key"));
        Assert.That(File.ReadAllText(paths.Creds), Is.EqualTo("creds"));
        Assert.That(Directory.Exists(Path.Combine(paths.DataDir, "medusae")), Is.False);
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);
        Assert.That(Directory.Exists(paths.RestoreStaging), Is.False);
    }

    // A zip entry can carry any Unix mode, for example setuid and world-writable.
    [Test]
    [Platform(Exclude = "Win")]
    public void ApplyPendingRestore_GivesRestoredFilesOnlyOwnerAccess()
    {
        var (paths, dbPath) = RestorePaths();
        File.WriteAllBytes(dbPath, SqliteBytes("old"));
        var hostileMode = Convert.ToInt32("104777", 8) << 16;
        using (var fs = File.Create(Path.Combine(paths.BackupDir, BACKUP_NAME)))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in new[] { ("NadekoBot.db", SqliteBytes("new")), ("data/bot.yml", "new"u8.ToArray()) })
            {
                var entry = archive.CreateEntry(name);
                entry.ExternalAttributes = hostileMode;
                using var w = entry.Open();
                w.Write(content);
            }
        }

        File.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(BACKUP_NAME).ToString());

        Assert.That(PendingRestore.Apply(paths, dbPath), Is.True);

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.That(ReadTag(dbPath), Is.EqualTo("new"));
        Assert.That(File.GetUnixFileMode(dbPath), Is.EqualTo(ownerOnly));
        Assert.That(File.GetUnixFileMode(Path.Combine(paths.DataDir, "bot.yml")), Is.EqualTo(ownerOnly));
    }

    // A managed container links data/ to a volume of its own.
    [Test]
    [Platform(Exclude = "Win")]
    public void ApplyPendingRestore_WorksThroughALinkedDataDir()
    {
        var volume = Path.Combine(_dir, "volume");
        Directory.CreateDirectory(volume);
        Directory.CreateSymbolicLink(Path.Combine(_dir, "data"), volume);
        var (paths, dbPath) = RestorePaths();
        WriteDbWithUncheckpointedWal(dbPath, "old");
        File.WriteAllText(Path.Combine(paths.DataDir, "bot.yml"), "old");
        WriteArchive(paths, ("NadekoBot.db", SqliteBytes("new")), ("data/bot.yml", "new"u8.ToArray()));
        File.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(BACKUP_NAME).ToString());

        Assert.That(BackupArchive.FreeBytes(paths.DataDir), Is.Not.Null);
        Assert.That(PendingRestore.Apply(paths, dbPath), Is.True);

        Assert.That(ReadTag(Path.Combine(volume, "NadekoBot.db")), Is.EqualTo("new"));
        Assert.That(ReadTag(Path.Combine(volume, "NadekoBot.db.pre-restore")), Is.EqualTo("old"));
        Assert.That(File.ReadAllText(Path.Combine(volume, "bot.yml")), Is.EqualTo("new"));
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);
        Assert.That(Directory.Exists(paths.RestoreStaging), Is.False);
        Assert.That(new DirectoryInfo(Path.Combine(_dir, "data")).LinkTarget, Is.EqualTo(volume));
    }

    [Test]
    public void ApplyPendingRestore_KeepsTheLiveDatabaseWhenTheRestoreCannotWork()
    {
        var (paths, dbPath) = RestorePaths();
        File.WriteAllText(dbPath, "old");

        // The marker names no valid archive.
        File.WriteAllText(paths.RestoreMarker, "../creds.yml");
        PendingRestore.Apply(paths, dbPath);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("old"));
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);

        // The database has a valid header but damaged pages.
        var damaged = SqliteBytes("new");
        Array.Fill(damaged, (byte)0xFF, 100, damaged.Length - 100);
        WriteArchive(paths, ("NadekoBot.db", damaged));
        File.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(BACKUP_NAME).ToString());
        PendingRestore.Apply(paths, dbPath);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("old"));
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);
        Assert.That(Directory.Exists(paths.RestoreStaging), Is.False);

        // Earlier attempts crashed the process; after the last one the restore gives up and puts the original back.
        WriteArchive(paths, ("NadekoBot.db", SqliteBytes("new")));
        File.Move(dbPath, dbPath + ".pre-restore");
        File.WriteAllText(dbPath, "partial");
        var exhausted = new RestoreMarker(BACKUP_NAME, true, PendingRestore.MAX_RESTORE_ATTEMPTS);
        File.WriteAllText(paths.RestoreMarker, exhausted.ToString());
        PendingRestore.Apply(paths, dbPath);
        Assert.That(File.ReadAllText(dbPath), Is.EqualTo("old"));
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);
    }

    [Test]
    public void ApplyPendingRestore_UsesTheEntryValidationAccepted()
    {
        var (paths, dbPath) = RestorePaths();
        File.WriteAllBytes(dbPath, SqliteBytes("old"));
        WriteArchive(paths, ("NadekoBot.db", []), ("data/NadekoBot.db", SqliteBytes("new")));

        Assert.That(BackupArchive.ValidateFile(Path.Combine(paths.BackupDir, BACKUP_NAME), BackupLimits.Restore, out _), Is.Null);

        File.WriteAllText(paths.RestoreMarker, RestoreMarker.Request(BACKUP_NAME).ToString());
        PendingRestore.Apply(paths, dbPath);

        Assert.That(ReadTag(dbPath), Is.EqualTo("new"));
    }

    [Test]
    public void ApplyPendingRestore_RetryKeepsTheOriginalDatabase()
    {
        var (paths, dbPath) = RestorePaths();
        WriteArchive(paths, ("NadekoBot.db", SqliteBytes("new")));

        // An earlier attempt set the database aside, then stopped after it moved the new one in.
        File.WriteAllBytes(dbPath + ".pre-restore", SqliteBytes("old"));
        File.WriteAllText(dbPath, "partial");
        File.WriteAllText(dbPath + "-wal", "partial-wal");
        File.WriteAllText(paths.RestoreMarker, new RestoreMarker(BACKUP_NAME, true, 1).ToString());

        PendingRestore.Apply(paths, dbPath);

        Assert.That(ReadTag(dbPath), Is.EqualTo("new"));
        Assert.That(File.Exists(dbPath + "-wal"), Is.False);
        Assert.That(ReadTag(dbPath + ".pre-restore"), Is.EqualTo("old"));
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);

        // An attempt which stopped before it recorded the set-aside left no live database.
        File.Delete(dbPath);
        File.WriteAllText(dbPath + "-wal", "stray");
        File.WriteAllText(paths.RestoreMarker, new RestoreMarker(BACKUP_NAME, false, 1).ToString());

        PendingRestore.Apply(paths, dbPath);

        Assert.That(ReadTag(dbPath), Is.EqualTo("new"));
        Assert.That(File.Exists(dbPath + "-wal"), Is.False);
        Assert.That(ReadTag(dbPath + ".pre-restore"), Is.EqualTo("old"));

        // When the archive is gone, a set-aside restore puts the original database back.
        File.Delete(Path.Combine(paths.BackupDir, BACKUP_NAME));
        File.WriteAllText(paths.RestoreMarker, new RestoreMarker(BACKUP_NAME, true, 1).ToString());
        File.Delete(dbPath);

        PendingRestore.Apply(paths, dbPath);

        Assert.That(ReadTag(dbPath), Is.EqualTo("old"));
        Assert.That(File.Exists(paths.RestoreMarker), Is.False);
    }

    [Test]
    public void RestoreMarker_ReadsEveryFormat()
    {
        Assert.That(RestoreMarker.Parse("a.zip"), Is.EqualTo(new RestoreMarker("a.zip", false, 0)));
        Assert.That(RestoreMarker.Parse("a.zip\nset-aside"), Is.EqualTo(new RestoreMarker("a.zip", true, 0)));
        var marker = new RestoreMarker("a.zip", true, 2);
        Assert.That(RestoreMarker.Parse(marker.ToString()), Is.EqualTo(marker));
    }

    [Test]
    public void ApiSettings_RoundTripWithoutDerivedFields()
    {
        var path = Path.Combine(_dir, "api.yml");
        ApiSettingsFile.Save(path, new ApiSettings().WithKey(KEY));

        var yml = File.ReadAllText(path);
        var loaded = ApiSettingsFile.LoadOrCreate(path);

        Assert.That(yml, Does.Not.Contain("hasKey"));
        Assert.That(loaded.ApiKey, Is.EqualTo(KEY));
        Assert.That(loaded.Address, Is.EqualTo("127.0.0.1"));
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    public async Task Gate_AppliesKeyShardAndReadinessInOrder()
    {
        const ApiAccess guild = ApiAccess.Key | ApiAccess.Bot | ApiAccess.Guild;
        (ApiAccess Access, string Key, string? Auth, BotPhase Phase, int Expected)[] cases =
        [
            (ApiAccess.KeyOnceSet, "", null, BotPhase.NoToken, 200),
            (ApiAccess.KeyOnceSet, KEY, null, BotPhase.NoToken, 401),
            (ApiAccess.Key, KEY, "Bearer " + KEY, BotPhase.Starting, 200),
            (ApiAccess.Key | ApiAccess.Bot, KEY, "Bearer " + KEY, BotPhase.Starting, 503),
            (guild, KEY, "Bearer " + KEY, BotPhase.Starting, 421),
            (guild, KEY, null, BotPhase.Starting, 401)
        ];

        var host = new HostContext(HostMode.Coordinated, 0, 4);
        foreach (var (access, key, auth, phase, expected) in cases)
        {
            var bridge = new BotBridge();
            bridge.SetPhase(phase);
            var gate = new ApiGateMiddleware(
                static ctx =>
                {
                    ctx.Response.StatusCode = 200;
                    return Task.CompletedTask;
                },
                new ApiKey(key),
                bridge,
                host);

            var http = new DefaultHttpContext();
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ApiAccessMetadata(access)), "test"));
            http.Request.RouteValues["guildId"] = (1UL << 22).ToString();
            if (auth is not null)
                http.Request.Headers.Authorization = auth;

            await gate.InvokeAsync(http);

            Assert.That(http.Response.StatusCode, Is.EqualTo(expected), $"{access} key={key.Length > 0} auth={auth is not null} {phase}");
        }
    }

    private (HostPaths Paths, string DbPath) RestorePaths()
    {
        var paths = new HostPaths(Path.Combine(_dir, "data"));
        Directory.CreateDirectory(paths.BackupDir);
        return (paths, Path.Combine(paths.DataDir, "NadekoBot.db"));
    }

    private static void WriteArchive(HostPaths paths, params (string Name, byte[] Content)[] entries)
    {
        using var fs = File.Create(Path.Combine(paths.BackupDir, BACKUP_NAME));
        Zip(entries).CopyTo(fs);
    }

    private byte[] SqliteBytes(string tag)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t(v TEXT); INSERT INTO t VALUES ($v);";
            cmd.Parameters.AddWithValue("$v", tag);
            cmd.ExecuteNonQuery();
        }

        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    // The last write is only in the -wal, as a bot which was killed leaves it.
    private void WriteDbWithUncheckpointedWal(string dbPath, string tag)
    {
        var source = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={source};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE t(v TEXT); INSERT INTO t VALUES ($v);";
        cmd.Parameters.AddWithValue("$v", tag);
        cmd.ExecuteNonQuery();

        File.Copy(source, dbPath, overwrite: true);
        File.Copy(source + "-wal", dbPath + "-wal", overwrite: true);
    }

    private static string ReadTag(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT v FROM t";
        return (string)cmd.ExecuteScalar()!;
    }

    private static MemoryStream Zip(params (string Name, byte[] Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = archive.CreateEntry(name).Open();
                w.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }
}
