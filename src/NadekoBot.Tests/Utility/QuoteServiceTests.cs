using System.Linq;
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
    public async Task RemoveAllByKeyword_DeletesOnlyMatchingQuotesOfTheServer()
    {
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "kek", "one");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "KEK", "two");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "other", "three");
        await _svc.AddQuoteAsync(OTHER_GUILD_ID, AUTHOR_ID, "a", "kek", "four");

        Assert.That(await _svc.RemoveAllByKeyword(GUILD_ID, "kek"), Is.EqualTo(2));
        Assert.That(await _svc.RemoveAllByKeyword(GUILD_ID, "kek"), Is.EqualTo(0));

        Assert.That((await _svc.GetGuildQuotesAsync(GUILD_ID)).Count, Is.EqualTo(1));
        Assert.That((await _svc.GetGuildQuotesAsync(OTHER_GUILD_ID)).Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Import_NormalizesKeywords_AndAcceptsMissingAuthorName()
    {
        const string input = """
                             hello:
                               - id: a
                                 aid: 5
                                 txt: hi there
                             empty:
                             """;

        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, input), Is.True);
        Assert.That(await _svc.ImportQuotesAsync(GUILD_ID, ""), Is.False);
        Assert.That((await _svc.GetGuildQuotesAsync(GUILD_ID)).Count, Is.EqualTo(1));

        var quote = await _svc.GetQuoteByKeywordAsync(GUILD_ID, "HELLO");
        Assert.That(quote, Is.Not.Null);
        Assert.That(quote!.Text, Is.EqualTo("hi there"));
        Assert.That(quote.AuthorName, Is.Empty);
    }

    [Test]
    public async Task Search_PagesThroughMatchesInIdOrder()
    {
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "cat", "meow");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "dog", "woof");
        await _svc.AddQuoteAsync(GUILD_ID, AUTHOR_ID, "a", "cat2", "purr");
        await _svc.AddQuoteAsync(OTHER_GUILD_ID, AUTHOR_ID, "a", "cat", "meow");

        Assert.That(await _svc.CountSearchQuotesAsync(GUILD_ID, "CAT"), Is.EqualTo(2));

        var second = await _svc.SearchQuotesAsync(GUILD_ID, "CAT", 1, 1);
        Assert.That(second.Single().Text, Is.EqualTo("purr"));
    }
}
