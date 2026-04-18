using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using NadekoBot.Common;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class ReplacementServiceTests
{
    private ReplacementPatternStore _store = null!;
    private ReplacementService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new ReplacementPatternStore();
        _svc = new ReplacementService(_store);
    }

    [Test]
    public void ReplacementInfo_PrecomputesMaskAndSlots()
    {
        var noParam = new ReplacementInfo("%time%", static () => new ValueTask<string>("now"));
        Assert.That(noParam.RequiredMask, Is.EqualTo(ContextMask.None));
        Assert.That(noParam.ParamSlotIndices, Is.Empty);

        var userOnly = new ReplacementInfo("%user%", static (IUser u) => new ValueTask<string>(u.Username));
        Assert.That(userOnly.RequiredMask, Is.EqualTo(ContextMask.User));
        Assert.That(userOnly.ParamSlotIndices, Is.EqualTo(new[] { ContextSlot.User }));

        var guildAndUser = new ReplacementInfo("%both%",
            static (IGuild g, IUser u) => new ValueTask<string>($"{g.Name}:{u.Username}"));
        Assert.That(guildAndUser.RequiredMask, Is.EqualTo(ContextMask.Guild | ContextMask.User));
        Assert.That(guildAndUser.ParamSlotIndices, Is.EqualTo(new[] { ContextSlot.Guild, ContextSlot.User }));
    }

    [Test]
    public void GetReplacementsForMask_FiltersByMask()
    {
        _store.Register("%test.static%", static () => "now");
        _store.Register<IUser>("%test.user%", static u => u.Username);

        var noContext = _store.GetReplacementsForMask(ContextMask.None);
        Assert.That(noContext.Any(r => r.Token == "%test.static%"), Is.True);
        Assert.That(noContext.Any(r => r.Token == "%test.user%"), Is.False);

        var withUser = _store.GetReplacementsForMask(ContextMask.User);
        Assert.That(withUser.Any(r => r.Token == "%test.static%"), Is.True);
        Assert.That(withUser.Any(r => r.Token == "%test.user%"), Is.True);

        var guildOnly = _store.GetReplacementsForMask(ContextMask.Guild);
        Assert.That(guildOnly.Any(r => r.Token == "%test.static%"), Is.True);
        Assert.That(guildOnly.Any(r => r.Token == "%test.user%"), Is.False);
    }

    [Test]
    public void GetReplacementsForMask_CachesSameInstance()
    {
        _store.Register("%time%", static () => "now");

        var first = _store.GetReplacementsForMask(ContextMask.None);
        var second = _store.GetReplacementsForMask(ContextMask.None);
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void Register_InvalidatesCache()
    {
        var before = _store.GetReplacementsForMask(ContextMask.None);
        var countBefore = before.Length;

        _store.Register("%test.new%", static () => "today");
        var after = _store.GetReplacementsForMask(ContextMask.None);
        Assert.That(after, Has.Length.EqualTo(countBefore + 1));
        Assert.That(after, Is.Not.SameAs(before));
    }

    [Test]
    public async Task ReplaceAsync_SubstitutesToken()
    {
        await _store.Register("%time%", static () => "now");

        var ctx = new ReplacementContext();
        var result = await _svc.ReplaceAsync("it is %time%", ctx);
        Assert.That(result, Is.EqualTo("it is now"));
    }

    [Test]
    public async Task ReplaceAsync_LeavesUnknownTokenAlone()
    {
        var ctx = new ReplacementContext();
        var result = await _svc.ReplaceAsync("hi %unknown%", ctx);
        Assert.That(result, Is.EqualTo("hi %unknown%"));
    }

    [Test]
    public async Task ReplaceAsync_MissingContext_SkipsRep()
    {
        await _store.Register<IUser>("%user%", static u => u.Username);

        var ctx = new ReplacementContext();
        var result = await _svc.ReplaceAsync("hi %user%", ctx);
        Assert.That(result, Is.EqualTo("hi %user%"));
    }

    [Test]
    public async Task ReplaceAsync_Override_TakesPrecedence()
    {
        await _store.Register("%name%", static () => "base");

        var ctx = new ReplacementContext().WithOverride("%name%", () => "override");
        var result = await _svc.ReplaceAsync("hi %name%", ctx);
        Assert.That(result, Is.EqualTo("hi override"));
    }

    [Test]
    public async Task ReplaceAsync_UserParam_ReceivesCorrectObject()
    {
        await _store.Register<IUser>("%test.uname%", static u => u.Username);

        var user = Substitute.For<IUser>();
        user.Username.Returns("TestUser");

        var ctx = new ReplacementContext(user: user);
        var result = await _svc.ReplaceAsync("hi %test.uname%", ctx);
        Assert.That(result, Is.EqualTo("hi TestUser"));
    }

    [Test]
    public async Task ReplaceAsync_RegexReplacement_Works()
    {
        var regex = new Regex(@"%test\.echo-(\w+)%");
        await _store.Register(regex, static m => m.Groups[1].Value);

        var ctx = new ReplacementContext();
        var result = await _svc.ReplaceAsync("say %test.echo-hello%", ctx);
        Assert.That(result, Is.EqualTo("say hello"));
    }
}
