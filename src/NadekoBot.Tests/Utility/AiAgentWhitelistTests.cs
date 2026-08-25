using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Nadeko.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class AiAgentWhitelistTests
{
    private const ulong USER_ID = 111;
    private const ulong SERVER_ID = 222;
    private const ulong ROLE_ID = 333;

    private TestDbService _db = null!;
    private AiAgentWhitelistService _svc = null!;

    [SetUp]
    public async Task Setup()
    {
        _db = new TestDbService();
        _svc = new AiAgentWhitelistService(_db, new EventPubSub());
        await _svc.OnReadyAsync();
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Toggle_AddsThenRemoves_AndPersists()
    {
        Assert.That(await _svc.ToggleAsync(AiAgentWhitelistType.User, USER_ID), Is.True);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.User, USER_ID), Is.True);

        await using (var ctx = _db.GetDbContext())
        {
            var rows = await ctx.GetTable<AiAgentWhitelistEntry>().ToListAsyncLinqToDB();
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].ItemId, Is.EqualTo(USER_ID));
            Assert.That(rows[0].Type, Is.EqualTo(AiAgentWhitelistType.User));
        }

        Assert.That(await _svc.ToggleAsync(AiAgentWhitelistType.User, USER_ID), Is.False);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.User, USER_ID), Is.False);

        await using (var ctx = _db.GetDbContext())
            Assert.That(await ctx.GetTable<AiAgentWhitelistEntry>().CountAsyncLinqToDB(), Is.EqualTo(0));
    }

    [Test]
    public async Task Types_AreIndependent()
    {
        await _svc.ToggleAsync(AiAgentWhitelistType.User, USER_ID);
        await _svc.ToggleAsync(AiAgentWhitelistType.Server, SERVER_ID);

        // Same raw id under a different type must not collide.
        await _svc.ToggleAsync(AiAgentWhitelistType.Role, USER_ID);

        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.User, USER_ID), Is.True);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.Server, SERVER_ID), Is.True);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.Role, USER_ID), Is.True);

        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.Server, USER_ID), Is.False);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.User, SERVER_ID), Is.False);

        // Removing one type leaves the others untouched.
        await _svc.ToggleAsync(AiAgentWhitelistType.User, USER_ID);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.User, USER_ID), Is.False);
        Assert.That(_svc.IsWhitelisted(AiAgentWhitelistType.Role, USER_ID), Is.True);
    }

    [Test]
    public async Task IsAnyWhitelisted_MatchesAnyOfTheGivenIds()
    {
        Assert.That(_svc.IsAnyWhitelisted(AiAgentWhitelistType.Role, new[] { ROLE_ID }), Is.False);

        await _svc.ToggleAsync(AiAgentWhitelistType.Role, ROLE_ID);

        Assert.That(_svc.IsAnyWhitelisted(AiAgentWhitelistType.Role, new[] { 900UL, ROLE_ID }), Is.True);
        Assert.That(_svc.IsAnyWhitelisted(AiAgentWhitelistType.Role, new[] { 900UL, 901UL }), Is.False);
        Assert.That(_svc.IsAnyWhitelisted(AiAgentWhitelistType.Role, System.Array.Empty<ulong>()), Is.False);
    }
}
