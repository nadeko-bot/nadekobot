#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NadekoBot.Common;
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
        _svc = new DangerousCommandsService(_db, new BotPolicy(false));
        _target = Path.Combine(_dir, "other.db");
        File.WriteAllBytes(Path.Combine(_dir, "bot.db"), []);
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
                     "PRAGMA journal_mode = DELETE",
                     "PRAGMA writable_schema = ON",
                     "SELECT load_extension('x')"
                 })
        {
            Assert.ThrowsAsync<SqliteException>(() => _svc.ExecuteSql(sql), sql);
            Assert.That(File.Exists(_target), Is.False, sql);
        }
    }

    [Test]
    public async Task SqlSelect_OnlyReads()
    {
        await _svc.ExecuteSql("CREATE TABLE t (x INTEGER)");
        await _svc.ExecuteSql("INSERT INTO t (x) VALUES (5)");
        await _svc.ExecuteSql("CREATE INDEX t_x ON t (x)");

        // The bot's shards keep the database open in WAL mode while the owner reads.
        await using var bot = new SqliteConnection(_db.ConnectionString);
        bot.Open();
        await using (var wal = bot.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL";
            wal.ExecuteNonQuery();
        }

        foreach (var sql in new[]
                 {
                     "INSERT INTO t (x) VALUES (6)",
                     "UPDATE t SET x = 1",
                     "DELETE FROM t",
                     "INSERT INTO t (x) VALUES (7) RETURNING x",
                     "DROP TABLE t",
                     "CREATE TABLE u (x)",
                     "CREATE TEMP TABLE u (x)",
                     "CREATE TRIGGER g AFTER INSERT ON t BEGIN DELETE FROM t; END",
                     $"ATTACH DATABASE '{_target}' AS x",
                     $"VACUUM INTO '{_target}'",
                     "VACUUM",
                     "REINDEX",
                     "ANALYZE",
                     "PRAGMA user_version = 5",
                     "PRAGMA query_only = OFF",
                     "SELECT * FROM pragma_database_list",
                     "BEGIN",
                     "SELECT load_extension('x')",
                     "SELECT zeroblob(100000000)",
                     "SELECT '" + new string('a', 10_000) + "'"
                 })
        {
            Assert.Throws<SqliteException>(() => _svc.SelectSql(sql), sql.Length > 100 ? sql[..100] : sql);
        }

        foreach (var sql in new[] { "SELECT 1; DELETE FROM t", "SELECT 1; SELECT 2", " -- nothing", "SELECT 1\0", "SELECT 1;\0DELETE FROM t" })
            Assert.Throws<DangerousCommandsService.NotOneStatementException>(() => _svc.SelectSql(sql), sql.Replace('\0', '0'));

        var tooBig = Assert.Throws<SqliteException>(() => _svc.SelectSql("SELECT hex(zeroblob(200000))"));
        Assert.That(tooBig!.SqliteErrorCode, Is.EqualTo(SQLitePCL.raw.SQLITE_TOOBIG));

        var empty = _svc.SelectSql("SELECT x AS a, x AS b FROM t WHERE 0");
        Assert.That((empty.ColumnNames, empty.Results.Count), Is.EqualTo((new List<string> { "a", "b" }, 0)));

        Assert.That(_svc.SelectSql("SELECT 1; -- note").Results, Is.EqualTo(new[] { new[] { "1" } }));
        Assert.That(File.Exists(_target), Is.False);
        Assert.That(_svc.SelectSql("SELECT x FROM t").Results, Is.EqualTo(new[] { new[] { "5" } }));
        Assert.That(_svc.SelectSql("SELECT name FROM sqlite_master WHERE type = 'table'").Results,
            Is.EqualTo(new[] { new[] { "t" } }));

        await using var cmd = bot.CreateCommand();
        cmd.CommandText = "SELECT (SELECT user_version FROM pragma_user_version), (SELECT count(*) FROM t)";
        await using var reader = cmd.ExecuteReader();
        reader.Read();
        Assert.That((reader.GetInt64(0), reader.GetInt64(1)), Is.EqualTo((0L, 1L)));
    }

    [Test]
    public void SqlSelect_StopsARunawayQueryAtTheTimeLimit()
    {
        // The native progress handler must keep its deadline alive through a collection.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var ex = Assert.Throws<SqliteException>(() => _svc.SelectSql(
            "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n) SELECT count(*) FROM n",
            TimeSpan.FromMilliseconds(300)));

        Assert.That(ex!.SqliteErrorCode, Is.EqualTo(SQLitePCL.raw.SQLITE_INTERRUPT));
    }

    [Test]
    public async Task SqlExec_StopsARunawayWriteAndKeepsNoRows()
    {
        await _svc.ExecuteSql("CREATE TABLE t (x INTEGER)");

        var ex = Assert.ThrowsAsync<SqliteException>(() => _svc.ExecuteSql(
            "WITH RECURSIVE r(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM r) INSERT INTO t SELECT i FROM r",
            TimeSpan.FromMilliseconds(300)));

        Assert.That(ex!.SqliteErrorCode, Is.EqualTo(SQLitePCL.raw.SQLITE_INTERRUPT));
        Assert.That(_svc.SelectSql("SELECT count(*) FROM t").Results[0][0], Is.EqualTo("0"));
    }

    [Test]
    public async Task SqlExec_WithALimitCapsTheGrowthOfTheDatabase()
    {
        await _svc.ExecuteSql("CREATE TABLE b (x BLOB)");
        var rows = (OwnerSqlConnection.EXEC_MAX_GROWTH_BYTES / 10_000_000) + 2;

        var ex = Assert.ThrowsAsync<SqliteException>(() => _svc.ExecuteSql(
            $"WITH RECURSIVE r(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM r LIMIT {rows}) INSERT INTO b SELECT zeroblob(10000000) FROM r",
            TimeSpan.FromSeconds(30)));

        Assert.That(ex!.SqliteErrorCode, Is.EqualTo(SQLitePCL.raw.SQLITE_FULL));
        Assert.That(await _svc.ExecuteSql("INSERT INTO b SELECT zeroblob(10000000)", null), Is.EqualTo(1));
    }

    [Test]
    public async Task SqlSelect_RunsOneQueryAtATime()
    {
        const string runaway = "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n) SELECT count(*) FROM n";
        using var start = new Barrier(2);

        Task<Exception?> RunInternal()
            => Task.Run(Exception? () =>
            {
                start.SignalAndWait();
                try
                {
                    _svc.SelectSql(runaway, TimeSpan.FromSeconds(3));
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

        var results = await Task.WhenAll(RunInternal(), RunInternal());

        Assert.That(results, Has.Exactly(1).TypeOf<DangerousCommandsService.QueryBusyException>());
        Assert.That(results, Has.Exactly(1).TypeOf<SqliteException>());
        Assert.That(_svc.SelectSql("SELECT 1").Results, Has.Count.EqualTo(1));
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
