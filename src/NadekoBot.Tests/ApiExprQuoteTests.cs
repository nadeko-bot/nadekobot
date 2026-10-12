using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Hosted.Endpoints;
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
