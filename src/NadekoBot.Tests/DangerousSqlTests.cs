#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NadekoBot.Db;
using NadekoBot.Modules.Owner.Services;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class DangerousSqlTests
{
    private string _dir = null!;
    private FileDbService _db = null!;
    private DangerousCommandsService _svc = null!;
    private string _target = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-sql-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new FileDbService(Path.Combine(_dir, "bot.db"));
        _svc = new DangerousCommandsService(_db);
        _target = Path.Combine(_dir, "other.db");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Test]
    public void OwnerSql_CannotTouchOtherFilesOrPragmas()
    {
        foreach (var sql in new[]
                 {
                     $"ATTACH DATABASE '{_target}' AS x",
                     $"VACUUM INTO '{_target}'",
                     "PRAGMA journal_mode = DELETE"
                 })
        {
            Assert.ThrowsAsync<SqliteException>(() => _svc.ExecuteSql(sql), sql);
            Assert.That(File.Exists(_target), Is.False, sql);
        }

        Assert.Throws<SqliteException>(() => _svc.SelectSql("SELECT * FROM pragma_database_list"));
    }

    [Test]
    public async Task OwnerSql_WorksOnTheBotsTables_AndBotQueriesKeepPragmas()
    {
        await _svc.ExecuteSql("CREATE TABLE t (x INTEGER)");
        Assert.That(await _svc.ExecuteSql("INSERT INTO t (x) VALUES (5)"), Is.EqualTo(1));
        Assert.That(_svc.SelectSql("SELECT x FROM t").Results[0][0], Is.EqualTo("5"));

        await using var conn = new SqliteConnection(_db.ConnectionString);
        conn.Open();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        Assert.DoesNotThrow(() => cmd.ExecuteScalar());
    }

    [Test]
    public void SqlSelect_StopsAtTheRowLimitAndCutsLongValues()
    {
        var rows = _svc.SelectSql(
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n LIMIT 5000) SELECT x FROM n");
        Assert.That((rows.Results.Count, rows.Truncated),
            Is.EqualTo((DangerousCommandsService.MAX_SELECT_ROWS, true)));

        var big = _svc.SelectSql("SELECT hex(randomblob(100000)), 'small'");
        Assert.That(big.Truncated, Is.True);
        Assert.That(big.Results[0][0].Length, Is.LessThanOrEqualTo(1000));
        Assert.That(big.Results[0][1], Is.EqualTo("small"));

        var small = _svc.SelectSql("SELECT 1, 'a'");
        Assert.That((small.Results.Count, small.Truncated), Is.EqualTo((1, false)));
    }

    private sealed class FileDbService(string path) : DbService
    {
        public override string ConnectionString { get; } = $"Data Source={path}";

        public override Task SetupAsync()
            => Task.CompletedTask;

        public override NadekoContext GetDbContext()
            => new(ConnectionString);
    }
}
