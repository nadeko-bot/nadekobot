using System.Linq;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using System.Threading.Tasks;
using NadekoBot.Modules.Utility;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class QuoteServiceTests
{
    private const ulong GUILD_ID = 111;
    private const ulong OTHER_GUILD_ID = 999;
    private const ulong AUTHOR_ID = 444;

    private TestDbService _db = null!;
    private QuoteService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new QuoteService(_db);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task SearchAndRemoveAllByKeyword_StayInTheServer()
    {
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "kek", "one");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "KEK", "two");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "other", "three");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "cat", "meow");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "dog", "woof");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "cat2", "purr");
        await _svc.AddQuoteAsync(OTHER_GUILD_ID, AUTHOR_ID, "a", "kek", "four");
        await _svc.AddQuoteAsync(OTHER_GUILD_ID, AUTHOR_ID, "a", "cat", "meow");

        Assert.That(await _svc.CountSearchQuotesAsync(GUILD_ID, "CAT"), Is.EqualTo(2));

        var second = await _svc.SearchQuotesAsync(GUILD_ID, "CAT", 1, 1);
        Assert.That(second.Single().Text, Is.EqualTo("purr"));

        Assert.That(await _svc.RemoveAllByKeyword(GUILD_ID, "kek"), Is.EqualTo(2));
        Assert.That(await _svc.RemoveAllByKeyword(GUILD_ID, "kek"), Is.EqualTo(0));

        Assert.That((await _svc.GetGuildQuotesAsync(GUILD_ID)).Count, Is.EqualTo(4));
        Assert.That((await _svc.GetGuildQuotesAsync(OTHER_GUILD_ID)).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Add_StopsAtTheServerLimit_AlsoForImport()
    {
        await using (var ctx = _db.GetDbContext())
        {
            await ctx.GetTable<NadekoBot.Db.Models.Quote>().BulkCopyAsync(
                Enumerable.Range(0, QuoteService.MAX_QUOTES_PER_SERVER - 1)
                          .Select(i => new NadekoBot.Db.Models.Quote
                          {
                              GuildId = GUILD_ID,
                              AuthorId = AUTHOR_ID,
                              AuthorName = "a",
                              Keyword = "K",
                              Text = "t" + i
                          }));
        }

        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, "a:\n  - txt: one\n  - txt: two\n"), Is.False);
        Assert.That(await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "last", "fits"), Is.Not.Null);
        Assert.That(await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "over", "full"), Is.Null);
        Assert.That(await _svc.AddQuoteAsync(OTHER_GUILD_ID, AUTHOR_ID, "a", "other", "fine"), Is.Not.Null);
    }

    [Test]
    public async Task Import_NormalizesKeywords_AndRefusesEmptyAliasesTooManyAndHugeInput()
    {
        const string aliased = """
                               a: &list
                                 - txt: spam
                               b: *list
                               """;

        var tooMany = new System.Text.StringBuilder("many:\n");
        for (var i = 0; i < 1001; i++)
            tooMany.Append("  - txt: q").Append(i).Append('\n');

        var huge = "big:\n  - txt: " + new string('x', 1024 * 1024) + "\n";

        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, aliased), Is.False);
        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, tooMany.ToString()), Is.False);
        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, huge), Is.False);
        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, ""), Is.False);
        Assert.That((await _svc.GetGuildQuotesAsync(GUILD_ID)).Count, Is.Zero);

        const string input = """
                             hello:
                               - id: a
                                 aid: 5
                                 txt: hi there
                             empty:
                             """;

        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, input), Is.True);
        Assert.That((await _svc.GetGuildQuotesAsync(GUILD_ID)).Count, Is.EqualTo(1));

        var quote = await _svc.GetQuoteByKeywordAsync(GUILD_ID, "HELLO");
        Assert.That(quote, Is.Not.Null);
        Assert.That(quote!.Text, Is.EqualTo("hi there"));
        Assert.That(quote.AuthorName, Is.Empty);
    }

    [Test]
    public async Task Edit_AllowsAuthorOrManager_OnlyInTheSameServer()
    {
        const ulong otherUserId = 555;
        var quote = await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "kek", "original");

        Assert.That(await _svc.EditQuoteAsync(GUILD_ID, otherUserId, false, quote!.Id, "stranger"), Is.Null);
        Assert.That(await _svc.EditQuoteAsync(OTHER_GUILD_ID, AUTHOR_ID, true, quote.Id, "elsewhere"), Is.Null);
        Assert.That((await _svc.GetQuoteByIdAsync(GUILD_ID, quote.Id))!.Text, Is.EqualTo("original"));

        var byAuthor = await _svc.EditQuoteAsync(GUILD_ID, AUTHOR_ID, false, quote.Id, "by author");
        Assert.That(byAuthor?.Text, Is.EqualTo("by author"));

        var byManager = await _svc.EditQuoteAsync(GUILD_ID, otherUserId, true, quote.Id, "by manager");
        Assert.That(byManager?.Keyword, Is.EqualTo("KEK"));
        Assert.That((await _svc.GetQuoteByIdAsync(GUILD_ID, quote.Id))!.Text, Is.EqualTo("by manager"));
    }
}
