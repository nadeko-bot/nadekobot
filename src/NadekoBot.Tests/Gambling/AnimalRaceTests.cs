#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Gambling.Common.AnimalRacing;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Games.Common;
using NadekoBot.Modules.Games.SavedGames;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public sealed class AnimalRaceTests
{
    private const ulong GUILD_SHARD_0 = 4194304UL * 2;
    private const ulong CHANNEL = 10;
    private const ulong USER_A = 1;
    private const ulong USER_B = 2;
    private const ulong USER_C = 3;
    private const ulong USER_D = 4;

    private TestDbService _db = null!;
    private FakeTimeProvider _time = null!;
    private SavedGameStore _store = null!;
    private AnimalRaceLedger _ledger = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // the strings return the key, so a test can see which note the bot chose
        var strings = Substitute.For<IBotStrings>();
        strings.GetText(Arg.Any<string>(), Arg.Any<ulong?>(), Arg.Any<object[]>())
            .Returns(static ci => ci.ArgAt<string>(0));

        _store = new SavedGameStore(_db, Substitute.For<ITxTracker>(), _time);
        _ledger = new AnimalRaceLedger(_db, Substitute.For<ITxTracker>(), strings, _time);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Ledger_ChargesEachJoinOnceAndPaysOrRefundsEachRaceOneTime()
    {
        foreach (var user in new[] { USER_A, USER_B, USER_C, USER_D })
            await SeedAsync(user, 100);

        Assert.That(await _ledger.JoinAsync(GUILD_SHARD_0, CHANNEL, USER_A, 10), Is.EqualTo(RaceJoinResult.NoRace));

        RaceAnimal[] animals = [new() { Icon = "a" }, new() { Icon = "b" }, new() { Icon = "c" }];
        var race = new AnimalRace(new RaceOptions(), animals);
        var row = await _store.AddAsync(GUILD_SHARD_0,
            CHANNEL,
            SavedGameKind.AnimalRace,
            AnimalRaceService.MAX_RACES_PER_SERVER,
            [1]);

        async Task<RaceJoinResult> JoinAsync(ulong userId, long bet)
            => (await race.JoinAsync(userId, "user", bet, () => _ledger.JoinAsync(GUILD_SHARD_0, CHANNEL, userId, bet))).Result;

        Assert.That(await JoinAsync(USER_A, 10), Is.EqualTo(RaceJoinResult.Joined));
        Assert.That(await JoinAsync(USER_A, 10), Is.EqualTo(RaceJoinResult.AlreadyJoined));
        // after a restart the race in memory is new, and the database still refuses a second join
        Assert.That(await _ledger.JoinAsync(GUILD_SHARD_0, CHANNEL, USER_A, 10), Is.EqualTo(RaceJoinResult.AlreadyJoined));
        Assert.That(await JoinAsync(USER_B, 0), Is.EqualTo(RaceJoinResult.Joined));
        Assert.That(await JoinAsync(USER_C, 1000), Is.EqualTo(RaceJoinResult.NotEnough));
        Assert.That(race.Users.Select(static u => (u.UserId, u.Animal.Icon)), Is.EqualTo(new[] { (USER_A, "a"), (USER_B, "b") }));
        Assert.That((await BalanceAsync(USER_A), await BalanceAsync(USER_B), await BalanceAsync(USER_C)),
            Is.EqualTo((90L, 100L, 100L)));

        // a race which runs again gives each member the animal of the join position
        var again = new AnimalRace(new RaceOptions(), animals);
        foreach (var entry in await _ledger.GetEntriesAsync(GUILD_SHARD_0, CHANNEL))
            again.AddRestored(entry.UserId, "user", entry.Amount);

        Assert.That(again.Users.Select(static u => (u.UserId, u.Animal.Icon, u.Bet)),
            Is.EqualTo(new[] { (USER_A, "a", 10L), (USER_B, "b", 0L) }));

        Assert.That(await _ledger.SettleAsync(row.Id, GUILD_SHARD_0, CHANNEL, USER_A, 1.5), Is.EqualTo(new RaceSettlement(true, 15)));
        Assert.That(await _ledger.SettleAsync(row.Id, GUILD_SHARD_0, CHANNEL, USER_A, 1.5), Is.EqualTo(new RaceSettlement(false, 0)));
        Assert.That(await BalanceAsync(USER_A), Is.EqualTo(105));

        // a race which cannot continue after a restart gives the bets back
        await _store.AddAsync(GUILD_SHARD_0, CHANNEL + 1, SavedGameKind.AnimalRace, AnimalRaceService.MAX_RACES_PER_SERVER, [1]);
        Assert.That(await _ledger.JoinAsync(GUILD_SHARD_0, CHANNEL + 1, USER_C, 40), Is.EqualTo(RaceJoinResult.Joined));
        _time.Advance(SavedGameRestoreService.MaxAge + TimeSpan.FromMinutes(1));

        var service = new AnimalRaceService(Substitute.For<DiscordSocketClient>(),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<IBotStrings>(),
            Substitute.For<ILocalization>(),
            Substitute.For<ICommandHandler>(),
            null!,
            null!,
            null!,
            _store,
            _ledger,
            null!,
            _time);
        var restorer = new SavedGameRestoreService(_store, null!, [service], _time);

        Assert.That(await restorer.RestoreAsync(await _store.LoadAsync(1, 0)), Is.Zero);
        Assert.That(await TransactionsAsync(USER_C), Is.EqualTo(new[]
        {
            (-40L, AnimalRaceTx.BET, ""),
            (40L, AnimalRaceTx.REFUND, strs.animal_race_note_refund.Key)
        }));

        // the bets of a race which no longer exists go back too
        var gone = await _store.AddAsync(GUILD_SHARD_0, CHANNEL + 2, SavedGameKind.AnimalRace, AnimalRaceService.MAX_RACES_PER_SERVER, [1]);
        await _ledger.JoinAsync(GUILD_SHARD_0, CHANNEL + 2, USER_D, 25);
        await _store.EndAsync(gone.Id);

        Assert.That(await _ledger.RefundOrphansAsync(2, 0), Is.EqualTo(1));
        Assert.That((await BalanceAsync(USER_C), await BalanceAsync(USER_D)), Is.EqualTo((100L, 100L)));
        Assert.That(await _store.LoadAsync(1, 0), Is.Empty);

        await using var ctx = _db.GetDbContext();
        Assert.That(await ctx.GetTable<AnimalRaceEntry>().CountAsyncLinqToDB(), Is.Zero);
    }

    private async Task SeedAsync(ulong userId, long amount)
    {
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<DiscordUser>()
            .InsertAsync(() => new()
            {
                UserId = userId,
                Username = "user",
                CurrencyAmount = amount
            });
    }

    private async Task<long> BalanceAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
            .Where(x => x.UserId == userId)
            .Select(x => x.CurrencyAmount)
            .FirstAsyncLinqToDB();
    }

    private async Task<(long Amount, string Extra, string Note)[]> TransactionsAsync(ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        var rows = await ctx.GetTable<CurrencyTransaction>()
            .Where(x => x.UserId == userId && x.Type == AnimalRaceTx.TYPE)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Amount, x.Extra, x.Note })
            .ToListAsyncLinqToDB();

        return rows.Select(static x => (x.Amount, x.Extra, x.Note)).ToArray();
    }
}
