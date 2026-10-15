using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Modules.NadekoExpressions;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiExprQuoteTests
{
    private const string BOT_MENTION = "<@100>";

    private TestDbService _db = null!;

    [SetUp]
    public void SetUp()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void TryParseReactions_AcceptsOnlyAFewEmojisOrEmotesTheBotHas()
    {
        using var client = new DiscordSocketClient();

        Assert.That(ExpressionEndpoints.TryParseReactions(["👍", "👎"], client, null, out var ok), Is.True);
        Assert.That(ok, Is.EqualTo(new[] { "👍", "👎" }));
        Assert.That(ExpressionEndpoints.TryParseReactions([], client, null, out var none), Is.True);
        Assert.That(none, Is.Empty);

        // the client is in no server, so it has no emotes to react with
        Assert.That(ExpressionEndpoints.TryParseReactions(["<:nadeko:123456789012345678>"], client, null, out _), Is.False);
        Assert.That(ExpressionEndpoints.TryParseReactions(["👍", "👍", "👍", "👍"], client, null, out _), Is.False);
        Assert.That(ExpressionEndpoints.TryParseReactions(["not an emoji"], client, null, out _), Is.False);
        Assert.That(ExpressionEndpoints.TryParseReactions(["👍@@@👎"], client, null, out _), Is.False);
        Assert.That(ExpressionEndpoints.TryParseReactions([" "], client, null, out _), Is.False);
    }

    [Test]
    public void BuildExprList_SortsPaginatesAndShowsTheStoredTrigger()
    {
        var exprs = new NadekoExpression[45];
        for (var i = 0; i < exprs.Length; i++)
            exprs[i] = new NadekoExpression { Id = i, Trigger = $"t{i:D2}", Response = "r", Reactions = "a@@@b" };
        exprs[44].Trigger = $"{BOT_MENTION} hi";

        var page0 = ExpressionEndpoints.BuildExprList(exprs, 0, BOT_MENTION);
        Assert.That(page0.Total, Is.EqualTo(45));
        Assert.That(page0.Items, Has.Count.EqualTo(20));
        Assert.That(page0.Items[0].Trigger, Is.EqualTo("%bot.mention% hi"));
        Assert.That(page0.Items[0].Reactions, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(page0.Items[1].Trigger, Is.EqualTo("t00"));

        var page2 = ExpressionEndpoints.BuildExprList(exprs, 2, BOT_MENTION);
        Assert.That(page2.Items, Has.Count.EqualTo(5));
        Assert.That(page2.Items[0].Trigger, Is.EqualTo("t39"));

        Assert.That(ExpressionEndpoints.BuildExprList(exprs, 5, BOT_MENTION).Items, Is.Empty);
    }

    [Test]
    public async Task SetTrigger_KeepsDuplicatesUnlessOverrideIsOnThenAsksBeforeReplacing()
    {
        var renamed = await InsertExprAsync(1, "old");
        var other = await InsertExprAsync(1, "hi");
        var otherServer = await InsertExprAsync(2, "hi");

        var dup = await NadekoExpressionsService.SetTriggerInDbAsync(_db, 1, renamed, "  HI ", false, false);
        Assert.That((dup.Status, dup.Expr?.Trigger), Is.EqualTo((SetTriggerStatus.Renamed, "hi")));
        Assert.That(dup.DeletedIds, Is.Empty);

        await NadekoExpressionsService.SetTriggerInDbAsync(_db, 1, renamed, "old", false, false);
        var taken = await NadekoExpressionsService.SetTriggerInDbAsync(_db, 1, renamed, "hi", true, false);
        Assert.That(taken.Status, Is.EqualTo(SetTriggerStatus.Taken));
        Assert.That(await ReadTriggerAsync(renamed), Is.EqualTo("old"));

        var replaced = await NadekoExpressionsService.SetTriggerInDbAsync(_db, 1, renamed, "hi", true, true);
        Assert.That(replaced.Status, Is.EqualTo(SetTriggerStatus.Renamed));
        Assert.That(replaced.DeletedIds, Is.EqualTo(new[] { other }));
        Assert.That(await ReadTriggerAsync(other), Is.Null);
        Assert.That(await ReadTriggerAsync(otherServer), Is.EqualTo("hi"));

        var wrongServer = await NadekoExpressionsService.SetTriggerInDbAsync(_db, 2, renamed, "x", true, true);
        Assert.That(wrongServer.Status, Is.EqualTo(SetTriggerStatus.NotFound));
        Assert.That(await ReadTriggerAsync(otherServer), Is.EqualTo("hi"));
    }

    private async Task<int> InsertExprAsync(ulong guildId, string trigger)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<NadekoExpression>()
                        .InsertWithInt32IdentityAsync(() => new NadekoExpression
                        {
                            GuildId = guildId,
                            Trigger = trigger,
                            Response = "r",
                            Reactions = "",
                            AutoDeleteTrigger = false,
                            DmResponse = false,
                            ContainsAnywhere = false,
                            AllowTarget = false
                        });
    }

    private async Task<string> ReadTriggerAsync(int id)
    {
        await using var ctx = _db.GetDbContext();
        var expr = await ctx.GetTable<NadekoExpression>().FirstOrDefaultAsyncLinqToDB(x => x.Id == id);
        return expr?.Trigger;
    }

    [Test]
    public async Task UpdateQuote_SetsOnlyTheGivenFieldsOfTheGuildsQuote()
    {
        int id;
        await using (var ctx = _db.GetDbContext())
        {
            id = await ctx.GetTable<Quote>()
                          .InsertWithInt32IdentityAsync(() => new Quote
                          {
                              GuildId = 1,
                              Keyword = "OLD",
                              Text = "old text",
                              AuthorName = "bob",
                              AuthorId = 2
                          });
        }

        var renamed = await QuoteEndpoints.UpdateQuoteAsync(_db, 1, id, "new", null);
        Assert.That((renamed?.Keyword, renamed?.Text), Is.EqualTo(("NEW", "old text")));

        var edited = await QuoteEndpoints.UpdateQuoteAsync(_db, 1, id, null, "new text");
        Assert.That((edited?.Keyword, edited?.Text), Is.EqualTo(("NEW", "new text")));

        Assert.That(await QuoteEndpoints.UpdateQuoteAsync(_db, 2, id, "x", "y"), Is.Null);

        await using var check = _db.GetDbContext();
        var stored = await check.GetTable<Quote>().FirstAsyncLinqToDB(x => x.Id == id);
        Assert.That((stored.Keyword, stored.Text), Is.EqualTo(("NEW", "new text")));
    }
}
