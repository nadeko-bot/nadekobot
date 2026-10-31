#nullable enable
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NadekoBot.Hosted;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class BackupImportTests
{
    // The 7z files were made with py7zr 1.0.0 from a database whose last row is only in its -wal.
    private static readonly string _fixtures = Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", "backups");

    private string _dir = string.Empty;
    private HostPaths _paths = null!;
    private BackupService _backups = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-import-" + Guid.NewGuid().ToString("N"));
        _paths = new HostPaths(Path.Combine(_dir, "data"));
        Directory.CreateDirectory(_paths.BackupDir);
        var time = TimeProvider.System;
        _backups = new BackupService(_paths, new ApiSettings(), new RestoreTarget(Path.Combine(_paths.DataDir, "NadekoBot.db")),
            new BootGuard(_paths, time), time);
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void Detect_NamesEachFormatByItsFirstBytes()
    {
        var (db, _) = DbWithWal();

        Assert.That(BackupImport.Detect(Zip(("NadekoBot.db", db))), Is.EqualTo(ImportFormat.Zip));
        Assert.That(BackupImport.Detect(Zip()), Is.EqualTo(ImportFormat.Zip));
        Assert.That(BackupImport.Detect(Gzip(db)), Is.EqualTo(ImportFormat.Gzip));
        Assert.That(BackupImport.Detect(File.ReadAllBytes(Path.Combine(_fixtures, "wal-import.7z"))),
            Is.EqualTo(ImportFormat.SevenZip));
        Assert.That(BackupImport.Detect(db), Is.EqualTo(ImportFormat.Sqlite));
        Assert.That(BackupImport.Detect(Tar(("NadekoBot.db", db))), Is.EqualTo(ImportFormat.Tar));
        Assert.That(BackupImport.Detect("hello"u8), Is.EqualTo(ImportFormat.Unknown));
        Assert.That(BackupImport.Detect([]), Is.EqualTo(ImportFormat.Unknown));
    }

    [Test]
    public async Task Import_TarGz_KeepsTheWalWritesAndOnlyTheFilesARestoreTakes()
    {
        var (db, wal) = DbWithWal();
        var upload = Gzip(Tar(
            ("./NadekoBot.db", db),
            ("./NadekoBot.db-wal", wal),
            ("./NadekoBot.db-shm", new byte[32]),
            ("./bot.yml", "prefix: '!'"u8.ToArray()),
            ("./data/games.yml", "a: 1"u8.ToArray()),
            ("./creds.yml", "token: secret"u8.ToArray()),
            ("./data/api.yml", "apiKey: x"u8.ToArray()),
            ("./data/medusae/x.dll", "MZ"u8.ToArray()),
            ("./NadekoBot/data/NadekoBot.db", "not this one"u8.ToArray())));

        var archive = await ImportOkAsync(upload);

        Assert.That(Entries(archive), Is.EquivalentTo(new[] { "NadekoBot.db", "data/bot.yml", "data/games.yml" }));
        Assert.That(ReadTags(archive), Is.EqualTo("main,wal"));
        Assert.That(BackupArchive.ValidateFile(archive, BackupLimits.Restore, out _), Is.Null);
        using var zip = ZipFile.OpenRead(archive);
        using var stored = zip.GetEntry("NadekoBot.db")!.Open();
        var header = new byte[20];
        stored.ReadExactly(header);
        Assert.That(header[18], Is.EqualTo(1), "the stored database needs no -wal");
    }

    [Test]
    public async Task Import_PlainTarBareDatabaseAndGzippedDatabase_AreStoredAsBackups()
    {
        var (db, wal) = DbWithWal();

        Assert.That(ReadTags(await ImportOkAsync(Tar(("data/NadekoBot.db", db), ("data/NadekoBot.db-wal", wal)))),
            Is.EqualTo("main,wal"));
        Assert.That(ReadTags(await ImportOkAsync(db)), Is.EqualTo("main"));
        Assert.That(ReadTags(await ImportOkAsync(Gzip(db))), Is.EqualTo("main"));
    }

    [Test]
    public async Task Import_AZipWithAWalIsRewrittenAndAPlainZipIsStoredAsUploaded()
    {
        var (db, wal) = DbWithWal();

        var rewritten = await ImportOkAsync(Zip(("data/NadekoBot.db", db), ("data/NadekoBot.db-wal", wal),
            ("data/bot.yml", "a: 1"u8.ToArray())));
        Assert.That(Entries(rewritten), Is.EquivalentTo(new[] { "NadekoBot.db", "data/bot.yml" }));
        Assert.That(ReadTags(rewritten), Is.EqualTo("main,wal"));

        var plain = Zip(("NadekoBot.db", db), ("data/bot.yml", "a: 1"u8.ToArray()));
        Assert.That(File.ReadAllBytes(await ImportOkAsync(plain)), Is.EqualTo(plain));
    }

    [Test]
    public async Task Import_SevenZip_IsReadAndAnEncryptedOneIsRefused()
    {
        var archive = await ImportOkAsync(File.ReadAllBytes(Path.Combine(_fixtures, "wal-import.7z")));

        Assert.That(Entries(archive), Is.EquivalentTo(new[] { "NadekoBot.db", "data/bot.yml" }));
        Assert.That(ReadTags(archive), Is.EqualTo("main,wal"));

        foreach (var name in new[] { "encrypted.7z", "encrypted-header.7z" })
        {
            var result = await ImportAsync(File.ReadAllBytes(Path.Combine(_fixtures, name)));
            Assert.That(result.Error, Is.EqualTo("encrypted_archive"), name);
        }
    }

    [Test]
    public async Task Import_SaysWhyItCannotUseAFileAndLeavesNothingBehind()
    {
        var (db, _) = DbWithWal();
        var damaged = new byte[8192];
        BackupArchive.SqliteHeader.CopyTo(damaged);
        var tarGz = Gzip(Tar(("NadekoBot.db", db), ("bot.yml", new byte[4096])));

        (byte[] Upload, string Error)[] cases =
        [
            ("just text"u8.ToArray(), "unknown_format"),
            (Gzip("just text"u8.ToArray()), "unknown_format"),
            (Tar(("data/bot.yml", "a: 1"u8.ToArray())), "missing_db"),
            (Tar(("NadekoBot.db", "not a database"u8.ToArray())), "not_sqlite"),
            (Tar(("NadekoBot.db", damaged)), "db_damaged"),
            (tarGz[..(tarGz.Length / 2)], "damaged_archive"),
            (Tar(("NadekoBot.db", db), ("data/bot.yml", "a: &x [1]\nb: *x"u8.ToArray())), "unsafe_entry")
        ];

        foreach (var (upload, error) in cases)
        {
            var result = await ImportAsync(upload);
            Assert.That(result.Error, Is.EqualTo(error), error);
            Assert.That(result.Status, Is.EqualTo(400), error);
        }

        Assert.That(Directory.GetFileSystemEntries(_paths.BackupDir), Is.Empty);
    }

    [Test]
    public void Stage_StopsAtTheUnpackedSizeLimit()
    {
        var (db, _) = DbWithWal();
        var limits = BackupLimits.ForImport(16 * 1024);
        var big = new byte[1024 * 1024];
        BackupArchive.SqliteHeader.CopyTo(big);

        (byte[] Upload, ImportFormat Format)[] cases =
        [
            (Tar(("NadekoBot.db", db), ("skipped/large.bin", big)), ImportFormat.Tar),
            (Gzip(big), ImportFormat.Gzip)
        ];

        foreach (var (upload, format) in cases)
        {
            var path = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(path, upload);
            var staged = BackupImport.Stage(path, format, Path.Combine(_dir, "stage-" + format), limits);
            Assert.That(staged.Error, Is.EqualTo("archive_too_large"), format.ToString());
        }
    }

    private async Task<BackupResult> ImportAsync(byte[] upload)
        => await _backups.ImportAsync(new MemoryStream(upload), upload.Length, CancellationToken.None);

    private async Task<string> ImportOkAsync(byte[] upload)
    {
        var result = await ImportAsync(upload);
        Assert.That(result.Error, Is.Null);
        return Path.Combine(_paths.BackupDir, result.Backup!.FileName);
    }

    private static string[] Entries(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.Select(static e => e.FullName).ToArray();
    }

    private string ReadTags(string archive)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        using (var zip = ZipFile.OpenRead(archive))
            BackupArchive.FindDbEntry(zip)!.ExtractToFile(path);

        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT group_concat(v, ',') FROM (SELECT v FROM t ORDER BY rowid)";
        return (string)cmd.ExecuteScalar()!;
    }

    // "main" is in the database file, "wal" only in its -wal, as a bot which was not stopped leaves them.
    private (byte[] Db, byte[] Wal) DbWithWal()
    {
        var source = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={source};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;
                          CREATE TABLE t(v TEXT); INSERT INTO t VALUES ('main');
                          PRAGMA wal_checkpoint(TRUNCATE);
                          INSERT INTO t VALUES ('wal');
                          """;
        cmd.ExecuteNonQuery();

        return (ReadShared(source), ReadShared(source + "-wal"));
    }

    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] Tar(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var writer = new TarWriter(ms, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) });
        }

        return ms.ToArray();
    }

    private static byte[] Gzip(byte[] content)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(content);

        return ms.ToArray();
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = archive.CreateEntry(name).Open();
                w.Write(content);
            }
        }

        return ms.ToArray();
    }
}
