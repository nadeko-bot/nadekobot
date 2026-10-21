using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;
using Result = NadekoBot.Modules.Searches.Services.StreamNotificationService.FollowStreamResult;

namespace NadekoBot.Tests.Searches;

public class StreamFollowTests
{
    private const ulong GUILD = 1;
    private const ulong CHANNEL = 10;
    private const ulong OTHER_CHANNEL = 11;
    private const int MAX = 2;

    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private static FollowedStream Stream(ulong channelId, string name)
        => new()
        {
            GuildId = GUILD,
            ChannelId = channelId,
            Type = FollowedStream.FType.Twitch,
            Username = name,
            PrettyName = name
        };

    private async Task<Result> AddAsync(FollowedStream fs, int max = MAX)
    {
        await using var ctx = _db.GetDbContext();
        return await StreamNotificationService.TryAddFollowAsync(ctx, fs, max);
    }

    [Test]
    public async Task Add_RefusesDuplicatesAndRespectsTheServerLimit()
    {
        var first = Stream(CHANNEL, "nadeko");
        Assert.That(await AddAsync(first), Is.EqualTo(Result.Followed));
        Assert.That(first.Id, Is.GreaterThan(0));

        Assert.That(await AddAsync(Stream(CHANNEL, "nadeko")), Is.EqualTo(Result.AlreadyFollowed));

        Assert.That(await AddAsync(Stream(OTHER_CHANNEL, "nadeko")), Is.EqualTo(Result.Followed));
        Assert.That(await AddAsync(Stream(CHANNEL, "other")), Is.EqualTo(Result.LimitReached));
        Assert.That(await AddAsync(Stream(CHANNEL, "other"), max: -1), Is.EqualTo(Result.Followed));

        await using var ctx = _db.GetDbContext();
        var rows = await ctx.GetTable<FollowedStream>().Where(x => x.GuildId == GUILD).CountAsyncLinqToDB();
        Assert.That(rows, Is.EqualTo(3));
    }
}
