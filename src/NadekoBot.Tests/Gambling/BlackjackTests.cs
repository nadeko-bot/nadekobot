using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Db.Models;
using Discord.WebSocket;
using NadekoBot.Extensions;
using NadekoBot.Modules.Gambling;
using NadekoBot.Modules.Gambling.Blackjack;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Games.Quests;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class BlackjackTests
{
    private const byte A = 1;
    private const byte J = 11;
    private const byte Q = 12;
    private const byte K = 13;

    private const ulong USER_A = 1;
    private const ulong USER_B = 2;
    private const ulong USER_C = 3;
    private const ulong USER_D = 4;
    private const ulong CHANNEL = 10;

    // guild ids whose shard is 0 and 1 of 2 shards
    private const ulong GUILD_SHARD_0 = 4194304UL * 2;
    private const ulong GUILD_SHARD_1 = 4194304UL * 3;

    private TestDbService _db = null!;
    private FakeTimeProvider _time = null!;
    private BlackjackLedger _ledger = null!;
    private IBotStrings _strings = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        // the strings return the key and its arguments, so a test can see which text the bot chose
        _strings = Substitute.For<IBotStrings>();
        _strings.GetText(Arg.Any<string>(), Arg.Any<ulong?>(), Arg.Any<object[]>())
            .Returns(static ci => ci.ArgAt<object[]>(2) is { Length: > 0 } args
                ? $"{ci.ArgAt<string>(0)}({string.Join(",", args)})"
                : ci.ArgAt<string>(0));

        var quests = new QuestService(_db,
            Substitute.For<IBotCache>(),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<DiscordSocketClient>());

        _ledger = new BlackjackLedger(_db, Substitute.For<ITxTracker>(), quests, _strings, _time);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void HandValue_CountsAnAceAsElevenOnlyWhileTheHandStaysAt21OrLess()
    {
        Assert.That(BlackjackHand.Evaluate(Cards(A, 6)), Is.EqualTo(new BjHandValue(17, true)));
        Assert.That(BlackjackHand.Evaluate(Cards(A, 6, 10)), Is.EqualTo(new BjHandValue(17, false)));
        Assert.That(BlackjackHand.Evaluate(Cards(A, A, 9)), Is.EqualTo(new BjHandValue(21, true)));
        Assert.That(BlackjackHand.Evaluate(Cards(K, Q, 5)).IsBust, Is.True);

        var natural = new BlackjackHand(10);
        natural.Add(Card(A));
        natural.Add(Card(J));
        Assert.That(natural.IsNatural, Is.True);
    }

    [Test]
    public void Dealer_GetsTheSecondCardAfterThePlayersAndHitsSoft17()
    {
        // player 10 8 stands; dealer 6, then A makes soft 17, the dealer hits a 2 and stands on soft 19
        var soft17 = Round([10], 10, 6, 8, A, 2);
        soft17.Deal();
        Assert.That(soft17.Dealer.Count, Is.EqualTo(1));
        soft17.Stand();
        Assert.That(soft17.Dealer.Value, Is.EqualTo(new BjHandValue(19, true)));
        Assert.That(soft17.Players[0].Hands[0].Result, Is.EqualTo(BjHandResult.Lose));

        // the table shows the dealer's cards one by one; the result shows only with the last card
        var renderer = CreateRenderer();
        var upCard = Render(renderer, soft17, 1);
        Assert.That(upCard.Description, Is.EqualTo(strs.bj_dealer_draws.Key));
        Assert.That(upCard.Fields[0].Name, Is.EqualTo("❔ bj_dealer · 6"));
        Assert.That(upCard.Fields[1].Name, Is.EqualTo("⏹ Mira · 18"));
        Assert.That(upCard.Fields[1].Value, Does.Not.Contain("→"));

        var twoCards = Render(renderer, soft17, 2);
        Assert.That(twoCards.Description, Is.EqualTo(strs.bj_dealer_draws.Key));
        Assert.That(twoCards.Fields[0].Name, Is.EqualTo("❔ bj_dealer · bj_soft(17)"));
        Assert.That(twoCards.Fields[1].Name, Is.EqualTo("⏹ Mira · 18"));

        var final = Render(renderer, soft17, soft17.Dealer.Count);
        Assert.That(final.Description, Does.StartWith(strs.bj_new_table("").Key));
        Assert.That(final.Fields[0].Name, Is.EqualTo("🏁 bj_dealer · bj_soft(19)"));
        Assert.That(final.Fields[1].Name, Is.EqualTo("❌ Mira · 18"));
        Assert.That(final.Fields[1].Value, Does.Not.Contain("→"));

        // player 10 9; dealer 10 7 stands on hard 17
        var hard17 = Round([10], 10, 10, 9, 7, 5);
        hard17.Deal();
        hard17.Stand();
        Assert.That(hard17.Dealer.Count, Is.EqualTo(2));
        Assert.That(hard17.Players[0].Hands[0].Result, Is.EqualTo(BjHandResult.Win));

        // player 10 6 hits a 10 and busts; the dealer still gets the second card, then stands on 16 with no hand left
        var allBust = Round([10], 10, 6, 6, 10, 10, 5);
        allBust.Deal();
        allBust.Hit();
        Assert.That(allBust.Phase, Is.EqualTo(BjRoundPhase.Finished));
        Assert.That(allBust.Dealer.Count, Is.EqualTo(2));
        Assert.That(allBust.Players[0].Hands[0].Result, Is.EqualTo(BjHandResult.Bust));
    }

    [Test]
    public void RepostGate_NeedsTenOtherMessagesAndFifteenSecondsBetweenReposts()
    {
        var gate = new BlackjackRepostGate(_time);
        gate.OnTablePosted(100);
        _time.Advance(TimeSpan.FromSeconds(15));

        // the table's own message does not count
        for (var i = 0; i < 20; i++)
            gate.OnMessage(100);

        for (ulong id = 1; id <= 9; id++)
            gate.OnMessage(id);
        Assert.That(gate.IsDue, Is.False);

        gate.OnMessage(10);
        Assert.That(gate.IsDue, Is.True);

        // a new table message resets the count, and the next repost waits 15 seconds
        gate.OnTablePosted(101);
        for (ulong id = 11; id <= 30; id++)
            gate.OnMessage(id);
        _time.Advance(TimeSpan.FromSeconds(14));
        Assert.That(gate.IsDue, Is.False);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(gate.IsDue, Is.True);
    }

    [Test]
    public void Payouts_BlackjackPaysThreeToTwoRoundedDownAndABustLosesWhenTheDealerBusts()
    {
        // seats: natural for 5, 17 stands, 16 hits and busts, 19 stands; dealer 6 10 draws a 10 and busts
        var round = Round([5, 10, 10, 10], A, 10, 10, 10, 6, K, 7, 6, 9, 10, 10, 10);
        round.Deal();
        round.Stand();
        round.Hit();
        round.Stand();

        Assert.That(round.Phase, Is.EqualTo(BjRoundPhase.Finished));
        Assert.That(Results(round), Is.EqualTo(new[]
        {
            (BjHandResult.Blackjack, 12L),
            (BjHandResult.Win, 20L),
            (BjHandResult.Bust, 0L),
            (BjHandResult.Win, 20L)
        }));

        // dealer 10 8: 18 pushes and 17 loses
        var second = Round([10, 10], 10, 10, 10, 8, 7, 8);
        second.Deal();
        second.Stand();
        second.Stand();
        Assert.That(Results(second), Is.EqualTo(new[] { (BjHandResult.Push, 10L), (BjHandResult.Lose, 0L) }));
    }

    [Test]
    public void DealerBlackjack_TakesDoubledAndSplitBetsInFullAndPushesAPlayerBlackjack()
    {
        // 5 6 doubles to 20, 8 8 splits to 8 2 and 8 3, A K is a natural; dealer A, then K after the players
        var round = Round([10, 10, 10], 5, 8, A, A, 6, 8, K, 9, 2, 3, K);
        round.Deal();
        round.Double();
        round.Split();
        round.Stand();
        round.Stand();

        Assert.That(round.Phase, Is.EqualTo(BjRoundPhase.Finished));
        Assert.That(round.DealerHasBlackjack, Is.True);
        Assert.That(Results(round), Is.EqualTo(new[]
        {
            (BjHandResult.Lose, 0L),
            (BjHandResult.Lose, 0L),
            (BjHandResult.Lose, 0L),
            (BjHandResult.Push, 10L)
        }));
        Assert.That((round.Players[0].TotalStake, round.Players[1].TotalStake), Is.EqualTo((20L, 20L)));
        Assert.That(BlackjackRound.GetPayouts(round.Players[0]), Is.Empty);
        Assert.That(BlackjackRound.GetPayouts(round.Players[1]), Is.Empty);
        Assert.That(BlackjackRound.GetPayouts(round.Players[2]),
            Is.EqualTo(new[] { new BlackjackPayout(BlackjackTx.PUSH, 10) }));
    }

    [Test]
    public void Split_NeedsTheSameRankAnd21AfterASplitPaysOneToOne()
    {
        var mixed = Round([10], K, 6, Q, 10);
        mixed.Deal();
        Assert.That(mixed.CanSplit, Is.False);

        // K K splits, the first hand gets an ace and stands at 21, the second gets a 9; dealer 6 10 busts
        var round = Round([10], K, 6, K, A, 9, 10, 10);
        round.Deal();
        Assert.That(round.CanSplit, Is.True);
        Assert.That(round.SplitCost, Is.EqualTo(10));
        round.Split();
        Assert.That(round.HandIndex, Is.EqualTo(1));
        round.Stand();

        var hands = round.Players[0].Hands;
        Assert.That(hands[0].IsNatural, Is.False);
        Assert.That(Results(round), Is.EqualTo(new[] { (BjHandResult.Win, 20L), (BjHandResult.Win, 20L) }));
        Assert.That(round.Players[0].TotalStake, Is.EqualTo(20));
    }

    [Test]
    public void Split_OnlyOnceAndASplitHandCannotDoubleWhileSplitAcesGetOneCardEach()
    {
        // 8 8 splits; the first hand gets another 8 but cannot split again, the second hand 8 3 cannot double
        var round = Round([10], 8, 6, 8, 8, 3, 10, 10, 10);
        round.Deal();
        round.Split();

        Assert.That(round.Players[0].Hands.Count, Is.EqualTo(BlackjackRound.MAX_HANDS));
        Assert.That((round.CanSplit, round.CanDouble), Is.EqualTo((false, false)));
        round.Stand();

        Assert.That(round.HandIndex, Is.EqualTo(1));
        Assert.That((round.CanSplit, round.CanDouble), Is.EqualTo((false, false)));
        round.Hit();

        // the second hand reached 21; dealer 6 10 busts on a 10
        Assert.That(round.Phase, Is.EqualTo(BjRoundPhase.Finished));
        Assert.That(Results(round), Is.EqualTo(new[] { (BjHandResult.Win, 20L), (BjHandResult.Win, 20L) }));
        Assert.That(round.Players[0].TotalStake, Is.EqualTo(20));

        // A A splits, each ace gets one card and the round goes to the dealer
        var aces = Round([10], A, 6, A, K, 9, 10, 10);
        aces.Deal();
        aces.Split();

        Assert.That(aces.Phase, Is.EqualTo(BjRoundPhase.Finished));
        Assert.That(aces.Players[0].Hands.Select(static h => (h.Count, h.IsStood)),
            Is.EqualTo(new[] { (2, true), (2, true) }));
        Assert.That(Results(aces), Is.EqualTo(new[] { (BjHandResult.Win, 20L), (BjHandResult.Win, 20L) }));
    }

    [Test]
    public void Double_WorksOnlyOnTheFirstTwoCards()
    {
        // 5 6 doubles and gets a 9, 2 3 hits a 6 to 11, which a three-card hand cannot double; dealer 6 10 busts
        var round = Round([10, 10], 5, 2, 6, 6, 3, 9, 6, 10, 10);
        round.Deal();
        Assert.That(round.DoubleCost, Is.EqualTo(10));
        round.Double();

        round.Hit();
        Assert.That(round.CanDouble, Is.False);
        round.Stand();

        Assert.That(Results(round), Is.EqualTo(new[] { (BjHandResult.Win, 40L), (BjHandResult.Win, 20L) }));
    }

    [Test]
    public void Double_IsAllowedOnlyOnNineToEleven()
    {
        // 5 4 is 9, 6 4 is 10, 9 2 is 11, 8 4 is 12, 5 3 is 8
        var nine = Round([10], 5, 5, 4);
        nine.Deal();
        var ten = Round([10], 6, 5, 4);
        ten.Deal();
        var eleven = Round([10], 9, 5, 2);
        eleven.Deal();
        var twelve = Round([10], 8, 5, 4);
        twelve.Deal();
        var eight = Round([10], 5, 5, 3);
        eight.Deal();

        Assert.That((nine.CanDouble, ten.CanDouble, eleven.CanDouble, twelve.CanDouble, eight.CanDouble),
            Is.EqualTo((true, true, true, false, false)));
    }

    [Test]
    public void Finish_StandsOpenHandsAndPlaysTheDealer()
    {
        // 10 8 stays at 18 when the table stops; the dealer 3 draws 6, 7 and 5 to 21
        var open = Round([10], 10, 3, 8, 6, 7, 5);
        open.Deal();
        open.Finish();

        Assert.That(open.Phase, Is.EqualTo(BjRoundPhase.Finished));
        Assert.That(Results(open), Is.EqualTo(new[] { (BjHandResult.Lose, 0L) }));

        // a natural still pays 3:2, and the dealer does not draw against it
        var natural = Round([10], A, 5, K, 10);
        natural.Deal();
        natural.Finish();

        Assert.That(Results(natural), Is.EqualTo(new[] { (BjHandResult.Blackjack, 25L) }));
        Assert.That(natural.Dealer.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Ledger_TakesStakesOnceAndPaysEachRoundOnlyOnce()
    {
        await SeedAsync(USER_A, 100);
        await SeedAsync(USER_B, 5);
        await SeedAsync(USER_C, 100);

        Assert.That(await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL, USER_A, 30), Is.EqualTo(BjSeatResult.Seated));
        Assert.That(await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL + 1, USER_A, 10), Is.EqualTo(BjSeatResult.AlreadySeated));
        Assert.That(await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL, USER_B, 10), Is.EqualTo(BjSeatResult.NotEnough));
        Assert.That(await _ledger.StakeAsync(GUILD_SHARD_0, CHANNEL, USER_A, 30, BjStakeKind.Double), Is.True);
        Assert.That(await _ledger.StakeAsync(GUILD_SHARD_0, CHANNEL, USER_A, 1000, BjStakeKind.Split), Is.False);
        Assert.That(await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL, USER_C, 10), Is.EqualTo(BjSeatResult.Seated));

        Assert.That((await BalanceAsync(USER_A), await BalanceAsync(USER_B)), Is.EqualTo((40L, 5L)));

        // one payout for both players: A is paid, C's payout does not match the stakes, so C gets the stakes back
        BjSettlement[] round =
        [
            new(USER_A, 60, [new(BlackjackTx.WIN, 120)]),
            new(USER_C, 999, [new(BlackjackTx.WIN, 1000)])
        ];
        Assert.That(await _ledger.SettleRoundAsync(GUILD_SHARD_0, CHANNEL, round), Is.EqualTo(2));
        Assert.That(await _ledger.SettleRoundAsync(GUILD_SHARD_0, CHANNEL, round), Is.Zero);
        Assert.That((await BalanceAsync(USER_A), await BalanceAsync(USER_C)), Is.EqualTo((160L, 100L)));

        Assert.That((await TransactionsAsync(USER_A)).Select(static t => (t.Amount, t.Extra)), Is.EqualTo(new[]
        {
            (-30L, BlackjackTx.BET),
            (-30L, BlackjackTx.DOUBLE),
            (120L, BlackjackTx.WIN)
        }));
        Assert.That(await TransactionsAsync(USER_C), Is.EqualTo(new[]
        {
            (-10L, BlackjackTx.BET, ""),
            (10L, BlackjackTx.BET_REFUND, strs.bj_note_mismatch.Key)
        }));
        Assert.That(await StakeCountAsync(), Is.Zero);
    }

    [Test]
    public async Task Rakeback_ClaimKeepsWhatAMemberOwesForRefundedBets()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<Rakeback>().InsertAsync(() => new Rakeback { UserId = USER_A, Amount = -440m });
            await ctx.GetTable<Rakeback>().InsertAsync(() => new Rakeback { UserId = USER_B, Amount = 50.7m });
        }

        var rakeback = new RakebackService(_db, Substitute.For<ICurrencyService>());

        Assert.That(await rakeback.ClaimRakebackAsync(USER_A), Is.Zero);
        Assert.That(await rakeback.GetRakebackAsync(USER_A), Is.EqualTo(-440));
        Assert.That(await rakeback.ClaimRakebackAsync(USER_B), Is.EqualTo(50));
        Assert.That(await rakeback.ClaimRakebackAsync(USER_B), Is.Zero);
    }

    [Test]
    public async Task StaleRefund_RefundsOldStakesOfThisShardExceptThoseOfSeatedMembers()
    {
        await SeedAsync(USER_A, 100);
        await SeedAsync(USER_B, 100);
        await SeedAsync(USER_C, 100);
        await SeedAsync(USER_D, 100);

        await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL, USER_A, 40);
        await _ledger.StakeAsync(GUILD_SHARD_0, CHANNEL, USER_A, 40, BjStakeKind.Split);
        await _ledger.StakeAsync(GUILD_SHARD_0, CHANNEL, USER_A, 10, BjStakeKind.Double);
        await _ledger.SeatAsync(GUILD_SHARD_1, CHANNEL, USER_B, 50);
        await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL + 2, USER_D, 30);

        _time.Advance(TimeSpan.FromMinutes(1));
        var cutoff = _time.GetUtcNow().UtcDateTime;
        await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL + 1, USER_C, 20);

        // D still sits at a live table, A does not
        static bool IsSeated(ulong userId)
            => userId == USER_D;

        Assert.That(await _ledger.RefundStaleAsync(2, 0, cutoff, BjRefundReason.Unpaid, IsSeated), Is.EqualTo(3));
        Assert.That(await _ledger.RefundStaleAsync(2, 0, cutoff, BjRefundReason.Unpaid, IsSeated), Is.Zero);

        Assert.That(
            (await BalanceAsync(USER_A), await BalanceAsync(USER_B), await BalanceAsync(USER_C), await BalanceAsync(USER_D)),
            Is.EqualTo((100L, 50L, 80L, 70L)));
        var refunds = (await TransactionsAsync(USER_A)).Where(static t => t.Amount > 0).ToArray();
        Assert.That(refunds.Select(static t => t.Extra), Is.EquivalentTo(new[]
        {
            BlackjackTx.BET_REFUND, BlackjackTx.SPLIT_REFUND, BlackjackTx.DOUBLE_REFUND
        }));
        Assert.That(refunds.Select(static t => t.Note), Is.All.EqualTo(strs.bj_note_unpaid.Key));
        Assert.That(await StakeCountAsync(), Is.EqualTo(3));

        // after a restart nobody sits at a table, so D's old stake goes back with the restart note
        Assert.That(await _ledger.RefundStaleAsync(2, 0, cutoff, BjRefundReason.Restart, static _ => false), Is.EqualTo(1));
        Assert.That(await BalanceAsync(USER_D), Is.EqualTo(100));
        Assert.That((await TransactionsAsync(USER_D)).Last(),
            Is.EqualTo((30L, BlackjackTx.BET_REFUND, strs.bj_note_restart.Key)));
        Assert.That(await StakeCountAsync(), Is.EqualTo(2));
    }

    [Test]
    public void Lobby_ShowsTheCountdownAndOnlyTheStartButton()
    {
        var deadline = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var state = new BjRenderState(GUILD_SHARD_0, "`.bj <amount>`", BjTablePhase.Joining, false, null,
            [new(USER_A, 100), new(USER_B, 50)], ["Mira", "Yuki"], deadline, 0, int.MaxValue);

        var (embed, components) = CreateRenderer().Render(state);

        var lines = embed.Description!.Split('\n');
        Assert.That(lines[0], Is.EqualTo($"{strs.bj_join_line("").Key}(`.bj <amount>`)"));
        Assert.That(lines[^2..], Is.EqualTo(new[] { "", $"{strs.bj_starts("").Key}(<t:{deadline.ToUnixTimeSeconds()}:R>)" }));

        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(static row => row.Components).OfType<ButtonComponent>().ToArray();
        Assert.That(buttons.Select(static b => b.CustomId),
            Is.EqualTo(new[] { BlackjackTable.ButtonId(BlackjackTable.ACT_START, 0) }));
    }

    private BlackjackRenderer CreateRenderer()
    {
        var localization = Substitute.For<ILocalization>();
        localization.GetCultureInfo(Arg.Any<ulong?>()).Returns(CultureInfo.InvariantCulture);

        var botConfig = new BotConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        var sender = Substitute.For<IMessageSenderService>();
        sender.CreateEmbed(Arg.Any<ulong?>()).Returns(_ => new NadekoEmbedBuilder(botConfig));

        var gambling = new GamblingConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        return new BlackjackRenderer(_strings, localization, gambling, sender);
    }

    private static Embed Render(BlackjackRenderer renderer, BlackjackRound round, int dealerShown)
    {
        var seats = new List<BjSeat>();
        var names = new List<string>();
        foreach (var player in round.Players)
        {
            seats.Add(new(player.UserId, player.Hands[0].BaseBet));
            names.Add("Mira");
        }

        var state = new BjRenderState(GUILD_SHARD_0, "`.bj <amount>`", BjTablePhase.Playing, false, round, seats, names,
            DateTimeOffset.UnixEpoch, 1, dealerShown);
        return renderer.Render(state).Embed;
    }

    private static BjCard Card(byte rank)
        => new(rank, 0);

    private static BjCard[] Cards(params byte[] ranks)
        => ranks.Select(Card).ToArray();

    // Cards go to each seat, the dealer's up card, each seat again, then every later draw in play order.
    private static BlackjackRound Round(long[] bets, params byte[] order)
    {
        var seats = new BjSeat[bets.Length];
        for (var i = 0; i < bets.Length; i++)
            seats[i] = new((ulong)i + 1, bets[i]);

        return new BlackjackRound(BlackjackShoe.FromOrder(Cards(order)), seats);
    }

    private static (BjHandResult, long)[] Results(BlackjackRound round)
    {
        var results = new List<(BjHandResult, long)>();
        foreach (var player in round.Players)
        {
            foreach (var hand in player.Hands)
                results.Add((hand.Result, hand.Payout));
        }

        return results.ToArray();
    }

    [Test]
    public void Restore_FinishesTheRoundExactlyAsTheTableWould()
    {
        // 5 6 doubles on 11 to 20, 8 8 splits into 8 2 and 8 3; dealer 10 then 7 stands on 17
        var played = Round([10, 10], 5, 8, 10, 6, 8, 9, 2, 3, 7);
        played.Deal();
        played.Double();
        played.Split();
        played.Stand();

        // the round is saved while the second hand of the split still plays
        var saved = BlackjackRoundStateCodec.Write(played);
        var restored = BlackjackRoundStateCodec.Read(saved);
        Assert.That(restored, Is.Not.Null);

        played.Stand();
        restored!.FinishRestored();

        Assert.That(Results(restored), Is.EqualTo(Results(played)));
        Assert.That(restored.Players[0].Hands[0].IsDoubled, Is.True);
        Assert.That(restored.Players[0].Hands[0].Bet, Is.EqualTo(20));
        Assert.That(restored.Players[1].Hands.Select(static h => h.IsFromSplit), Is.All.True);

        // a round saved after the dealer drew gives the same results from its cards
        var finished = BlackjackRoundStateCodec.Read(BlackjackRoundStateCodec.Write(played));
        Assert.That(finished, Is.Not.Null);
        finished!.FinishRestored();
        Assert.That(Results(finished), Is.EqualTo(Results(played)));
    }

    [Test]
    public void Read_RefusesBuffersItCannotTrust()
    {
        var round = Round([10, 10], 5, 8, 10, 6, 8);
        round.Deal();
        var saved = BlackjackRoundStateCodec.Write(round);

        Assert.That(BlackjackRoundStateCodec.Read([]), Is.Null);

        var badVersion = (byte[])saved.Clone();
        badVersion[0] = 2;
        Assert.That(BlackjackRoundStateCodec.Read(badVersion), Is.Null);

        // a saved round is never in the dealing phase
        var dealing = (byte[])saved.Clone();
        dealing[1] = 0;
        Assert.That(BlackjackRoundStateCodec.Read(dealing), Is.Null);

        // the dealer's card has a rank which does not exist
        var badRank = (byte[])saved.Clone();
        badRank[5] = 14;
        Assert.That(BlackjackRoundStateCodec.Read(badRank), Is.Null);

        for (var length = 1; length < saved.Length; length++)
            Assert.That(BlackjackRoundStateCodec.Read(saved.AsSpan(0, length)), Is.Null, $"truncated at {length}");
    }

    [Test]
    public async Task RestartSettle_PaysTheSavedRoundOnceAndDeletesItsState()
    {
        await SeedAsync(USER_A, 100);
        await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL, USER_A, 40);
        await _ledger.SaveRoundAsync(GUILD_SHARD_0, CHANNEL, [1, 2, 3]);

        var settlements = new[] { new BjSettlement(USER_A, 40, [new(BlackjackTx.WIN, 80)]) };
        Assert.That(await _ledger.SettleRoundAsync(GUILD_SHARD_0, CHANNEL, settlements), Is.EqualTo(1));

        // a second call finds no stakes, so it pays nothing
        Assert.That(await _ledger.SettleRoundAsync(GUILD_SHARD_0, CHANNEL, settlements), Is.Zero);

        Assert.That(await BalanceAsync(USER_A), Is.EqualTo(140));
        Assert.That(await StakeCountAsync(), Is.Zero);
        Assert.That(await RoundStateCountAsync(), Is.Zero);
    }

    [Test]
    public async Task StaleRefund_LeavesTheStakesOfASavedRoundForTheRestartSettle()
    {
        await SeedAsync(USER_A, 100);
        await SeedAsync(USER_B, 100);
        await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL, USER_A, 40);
        await _ledger.SeatAsync(GUILD_SHARD_0, CHANNEL + 1, USER_B, 30);
        await _ledger.SaveRoundAsync(GUILD_SHARD_0, CHANNEL, [1, 2, 3]);

        _time.Advance(TimeSpan.FromMinutes(1));
        var cutoff = _time.GetUtcNow().UtcDateTime;

        Assert.That(await _ledger.RefundStaleAsync(2, 0, cutoff, BjRefundReason.Restart, static _ => false),
            Is.EqualTo(1));

        // the seat whose round was saved keeps its stake, the other one goes back
        Assert.That((await BalanceAsync(USER_A), await BalanceAsync(USER_B)), Is.EqualTo((60L, 100L)));
        Assert.That(await StakeCountAsync(), Is.EqualTo(1));
        Assert.That(await RoundStateCountAsync(), Is.EqualTo(1));

        // a saved round which cannot be read is deleted, and the next sweep refunds its stake
        await _ledger.DeleteRoundAsync(GUILD_SHARD_0, CHANNEL);
        Assert.That(await _ledger.RefundStaleAsync(2, 0, cutoff, BjRefundReason.Restart, static _ => false),
            Is.EqualTo(1));
        Assert.That(await BalanceAsync(USER_A), Is.EqualTo(100));
        Assert.That(await RoundStateCountAsync(), Is.Zero);
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
            .Where(x => x.UserId == userId && x.Type == BlackjackTx.TYPE)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Amount, x.Extra, x.Note })
            .ToListAsyncLinqToDB();

        return rows.Select(static x => (x.Amount, x.Extra, x.Note)).ToArray();
    }

    private async Task<int> StakeCountAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<BlackjackStake>().CountAsyncLinqToDB();
    }

    private async Task<int> RoundStateCountAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<BlackjackRoundState>().CountAsyncLinqToDB();
    }
}
