using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Common;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Db;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Waifus.Waifu;
using NadekoBot.Modules.Waifus.Waifu.Db;
using NadekoBot.Modules.Patronage;
using NadekoBot.Services;
using NSubstitute;

namespace NadekoBot.Tests.Waifu;

/// <summary>
/// SQLite context that uses a shared in-memory connection.
/// </summary>
public sealed class TestSqliteContext : NadekoContext
{
    private readonly SqliteConnection _connection;

    protected override string CurrencyTransactionOtherIdDefaultValue => "NULL";

    public TestSqliteContext(SqliteConnection connection)
    {
        _connection = connection;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseSqlite(_connection);
    }
}

/// <summary>
/// Test DbService that creates SQLite in-memory contexts with a persistent shared connection.
/// </summary>
public sealed class TestDbService : DbService, IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbService()
    {
        LinqToDBForEFTools.Initialize();
        Configuration.Linq.DisableQueryCache = true;

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create schema immediately
        using var ctx = new TestSqliteContext(_connection);
        ctx.Database.EnsureCreated();
    }

    public override NadekoContext GetDbContext()
        => new TestSqliteContext(_connection);

    public override Task SetupAsync()
        => Task.CompletedTask;

    public override DbContext CreateRawDbContext(string dbType, string connString)
        => new TestSqliteContext(_connection);

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}

/// <summary>
/// Helper for inserting test data directly via DbContext.
/// </summary>
public static class WaifuTestHelper
{
    /// <summary>
    /// Creates a real WaifuConfigService backed by a temp file for testing.
    /// </summary>
    public static WaifuConfigService CreateConfigService()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "nadeko_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var tmpPath = Path.Combine(tmpDir, "waifu.yml");
        return new WaifuConfigService(tmpPath, new YamlSeria(), Substitute.For<IPubSub>());
    }

    /// <summary>
    /// Creates a mock IPatronageService that returns no patron (non-patron defaults).
    /// </summary>
    public static IPatronageService CreatePatronageService()
    {
        var ps = Substitute.For<IPatronageService>();
        ps.GetPatronAsync(Arg.Any<ulong>()).Returns((Patron?)null);
        return ps;
    }

    /// <summary>
    /// Inserts a WaifuInfo record with configurable defaults.
    /// </summary>
    public static async Task<WaifuInfo> CreateWaifu(
        NadekoContext ctx,
        ulong userId,
        int mood = 500,
        int food = 500,
        int fee = 5,
        long price = 1000,
        ulong? managerId = null,
        long returnsCap = 1_000_000,
        bool isHubby = false)
    {
        await ctx.GetTable<WaifuInfo>()
            .InsertAsync(() => new WaifuInfo
            {
                UserId = userId,
                Mood = mood,
                Food = food,
                WaifuFeePercent = fee,
                Price = price,
                ManagerUserId = managerId,
                ReturnsCap = returnsCap,
                IsHubby = isHubby,
                LastDecayTime = DateTime.UtcNow,
                TotalProduced = 0
            });

        return await ctx.GetTable<WaifuInfo>()
            .FirstAsyncLinqToDB(x => x.UserId == userId);
    }

    /// <summary>
    /// Inserts a WaifuFan record.
    /// </summary>
    public static async Task<WaifuFan> CreateFan(
        NadekoContext ctx,
        ulong userId,
        ulong waifuUserId)
    {
        await ctx.GetTable<WaifuFan>()
            .InsertAsync(() => new WaifuFan
            {
                UserId = userId,
                WaifuUserId = waifuUserId,
                DelegatedAt = DateTime.UtcNow
            });

        return await ctx.GetTable<WaifuFan>()
            .FirstAsyncLinqToDB(x => x.UserId == userId && x.WaifuUserId == waifuUserId);
    }

    /// <summary>
    /// Inserts or updates a BankUser record with the given balance.
    /// </summary>
    public static async Task SetBankBalance(
        NadekoContext ctx,
        ulong userId,
        long balance)
    {
        var existing = await ctx.GetTable<BankUser>()
            .FirstOrDefaultAsyncLinqToDB(x => x.UserId == userId);

        if (existing is not null)
        {
            await ctx.GetTable<BankUser>()
                .Where(x => x.UserId == userId)
                .Set(x => x.Balance, balance)
                .UpdateAsync();
        }
        else
        {
            await ctx.GetTable<BankUser>()
                .InsertAsync(() => new BankUser
                {
                    UserId = userId,
                    Balance = balance
                });
        }
    }

    /// <summary>
    /// Inserts a WaifuCycleSnapshot record.
    /// </summary>
    public static async Task CreateCycleSnapshot(
        NadekoContext ctx,
        int cycleNumber,
        ulong waifuUserId,
        ulong userId,
        long balance)
    {
        await ctx.GetTable<WaifuCycleSnapshot>()
            .InsertAsync(() => new WaifuCycleSnapshot
            {
                CycleNumber = cycleNumber,
                WaifuUserId = waifuUserId,
                UserId = userId,
                SnapshotBalance = balance
            });
    }

    /// <summary>
    /// Inserts a WaifuCycle record.
    /// </summary>
    public static async Task CreateCycleRecord(
        NadekoContext ctx,
        ulong waifuUserId,
        int cycleNumber,
        ulong managerUserId,
        long totalBacked,
        int waifuFeePercent = 5,
        long returnsCap = 1_000_000_000,
        double managerCutPercent = 0.15,
        long price = 1000,
        bool processed = true)
    {
        await ctx.GetTable<WaifuCycle>()
            .InsertAsync(() => new WaifuCycle
            {
                WaifuUserId = waifuUserId,
                CycleNumber = cycleNumber,
                ManagerUserId = managerUserId,
                WaifuFeePercent = waifuFeePercent,
                ReturnsCap = returnsCap,
                ManagerCutPercent = managerCutPercent,
                Price = price,
                TotalBacked = totalBacked,
                Processed = processed,
                ProcessedAt = DateTime.UtcNow
            });
    }
}
