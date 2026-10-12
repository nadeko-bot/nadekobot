#nullable enable
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiGreetTests
{
    private const ulong GUILD_ID = 1;
    private const ulong OTHER_GUILD_ID = 2;

    private TestDbService? _db;

    [SetUp]
    public void SetUp()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db?.Dispose();

    [Test]
    public void CheckGreetChannel_EnablingNeedsAUsableChannelExceptForDms()
    {
        Assert.That(GreetEndpoints.CheckGreetChannel(GreetType.Greet, null, false, true, false),
            Is.SameAs(ApiErrors.ChannelRequired));
        Assert.That(GreetEndpoints.CheckGreetChannel(GreetType.Bye, 5, false, true, false),
            Is.SameAs(ApiErrors.ChannelNotFound), "a stored channel which is gone");
        Assert.That(GreetEndpoints.CheckGreetChannel(GreetType.Greet, 5, true, false, false),
            Is.SameAs(ApiErrors.ChannelNotFound), "a given voice or unknown channel");

        Assert.That(GreetEndpoints.CheckGreetChannel(GreetType.GreetDm, null, false, true, false), Is.Null);
        Assert.That(GreetEndpoints.CheckGreetChannel(GreetType.Greet, 5, false, false, false), Is.Null,
            "disabling with a stale stored channel");
        Assert.That(GreetEndpoints.CheckGreetChannel(GreetType.Boost, 5, true, true, true), Is.Null);
    }

    [Test]
    public async Task ReadGreetDtos_ListsEveryTypeWithDefaultsForUnsetOnes()
    {
        await using (var ctx = _db!.GetDbContext())
        {
            await ctx.GetTable<GreetSettings>()
                     .InsertAsync(() => new GreetSettings
                     {
                         GuildId = GUILD_ID,
                         GreetType = GreetType.Bye,
                         IsEnabled = true,
                         ChannelId = 42,
                         MessageText = "bye %user.name%",
                         AutoDeleteTimer = 30
                     });
            await ctx.GetTable<GreetSettings>()
                     .InsertAsync(() => new GreetSettings
                     {
                         GuildId = OTHER_GUILD_ID,
                         GreetType = GreetType.Greet,
                         IsEnabled = true,
                         ChannelId = 7
                     });
        }

        var dtos = await GreetEndpoints.ReadGreetDtosAsync(_db, GUILD_ID);

        Assert.That(dtos.Select(static d => d.Type), Is.EquivalentTo(new[] { "greet", "greetdm", "bye", "boost" }));

        var bye = dtos.Single(static d => d.Type == "bye");
        Assert.That(bye, Is.EqualTo(new GreetDto("bye", true, "42", "bye %user.name%", 30)));

        var greet = dtos.Single(static d => d.Type == "greet");
        Assert.That(greet, Is.EqualTo(new GreetDto("greet", false, null, GreetService.GetDefaultGreet(GreetType.Greet), 0)));
    }
}
