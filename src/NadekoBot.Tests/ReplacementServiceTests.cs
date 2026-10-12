using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NadekoBot.Common;
using NadekoBot.Extensions;
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
    public void GetReplacementsForMask_CachesUntilRegister()
    {
        _store.Register("%time%", static () => "now");

        var first = _store.GetReplacementsForMask(ContextMask.None);
        var second = _store.GetReplacementsForMask(ContextMask.None);
        Assert.That(second, Is.SameAs(first));

        _store.Register("%test.new%", static () => "today");
        var after = _store.GetReplacementsForMask(ContextMask.None);
        Assert.That(after, Has.Length.EqualTo(first.Length + 1));
        Assert.That(after, Is.Not.SameAs(first));
    }

    [Test]
    public async Task ReplaceAsync_SubstitutesTokensAndKeepsSurroundingText()
    {
        await _store.Register("%time%", static () => "now");
        await _store.Register("%test.a%", static () => "x");
        await _store.Register("%test.b%", static () => "y");

        var ctx = new ReplacementContext();
        Assert.That(await _svc.ReplaceAsync("it is %time%", ctx), Is.EqualTo("it is now"));
        Assert.That(await _svc.ReplaceAsync("%test.a% and %test.b%", ctx), Is.EqualTo("x and y"));
        Assert.That(await _svc.ReplaceAsync("prefix %test.a% middle %test.b% suffix", ctx),
            Is.EqualTo("prefix x middle y suffix"));
    }

    [Test]
    public async Task ReplaceAsync_LeavesUnresolvedTextAlone()
    {
        await _store.Register<IUser>("%user%", static u => u.Username);

        var ctx = new ReplacementContext();
        Assert.That(await _svc.ReplaceAsync("hi %unknown%", ctx), Is.EqualTo("hi %unknown%"));
        Assert.That(await _svc.ReplaceAsync("hi %user%", ctx), Is.EqualTo("hi %user%"));
        Assert.That(await _svc.ReplaceAsync("hi %foo", ctx), Is.EqualTo("hi %foo"));
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
    public async Task ReplaceAsync_ConsecutivePercents_HandledCorrectly()
    {
        await _store.Register("%test.k%", static () => "V");

        var ctx = new ReplacementContext();
        var result = await _svc.ReplaceAsync("%%%test.k%", ctx);
        Assert.That(result, Is.EqualTo("%%V"));
    }

    [Test]
    public async Task MemberNames_CannotPingEveryone()
    {
        var user = Substitute.For<IGuildUser>();
        user.Username.Returns("@here");
        user.DisplayName.Returns("@everyone <@&123>");

        var result = await _svc.ReplaceAsync("hi %user.name% %user.displayname%", new ReplacementContext(user: user));
        Assert.That(result.HasMassMention(), Is.False);

        Assert.That("hi @everyone".HasMassMention(), Is.True);
        Assert.That("hi <@&123>".HasMassMention(), Is.True);
        Assert.That("hi <@123> %user%".HasMassMention(), Is.False);
    }

    [Test]
    public async Task Template_PingsOnlyWhatTheAuthorWrote()
    {
        await _store.Register("%test.value%", static () => "%test.inner% @here");
        await _store.Register("%test.inner%", static () => "inner");
        await _store.Register("%test.empty%", static () => string.Empty);

        const string template = "%test.value% @every%test.empty%one <@&5>";
        var result = await _svc.ReplaceAsync(template, new ReplacementContext());
        Assert.That(result, Is.EqualTo("%test.inner% @here @everyone <@&5>"));

        var mentions = TemplateMentions.FromTemplate(template);
        Assert.That(mentions.AllowedTypes, Is.EqualTo(AllowedMentionTypes.Users));
        Assert.That(mentions.RoleIds, Is.EqualTo(new ulong[] { 5 }));

        var rich = TemplateMentions.FromTemplate("""{"content":"hi @everyone","embeds":[{"description":"@here"}]}""");
        Assert.That(rich.AllowedTypes, Is.EqualTo(AllowedMentionTypes.Users | AllowedMentionTypes.Everyone));

        Assert.That("hi @everyone <@&5>".SanitizeTemplateMentions().HasMassMention(), Is.False);
        var escaped = """{"content":"\u0040here <@\u00265>","embeds":[{"description":"x"}]}""".SanitizeTemplateMentions();
        Assert.That(((SmartEmbedTextArray)SmartText.CreateFrom(escaped)).Content!.HasMassMention(), Is.False);
    }

    [Test]
    public async Task ReplaceAsync_ContextParams_ReceiveCorrectObjects()
    {
        await _store.Register<IUser>("%test.uname%", static u => u.Username);
        await _store.Register<IGuild>("%test.guild%", static g => g.Name);
        await _store.Register<IMessageChannel>("%test.ch%", static ch => ch.Name);
        await _store.Register<IGuildUser>("%test.gu%", static gu => gu.DisplayName);

        var user = Substitute.For<IUser>();
        user.Username.Returns("TestUser");
        Assert.That(await _svc.ReplaceAsync("hi %test.uname%", new ReplacementContext(user: user)),
            Is.EqualTo("hi TestUser"));

        var guild = Substitute.For<IGuild>();
        guild.Name.Returns("TestGuild");
        Assert.That(await _svc.ReplaceAsync("in %test.guild%", new ReplacementContext(guild: guild)),
            Is.EqualTo("in TestGuild"));

        var ch = Substitute.For<IMessageChannel>();
        ch.Name.Returns("general");
        Assert.That(await _svc.ReplaceAsync("in %test.ch%", new ReplacementContext(channel: ch)),
            Is.EqualTo("in general"));

        var gu = Substitute.For<IGuildUser>();
        gu.DisplayName.Returns("Nick");
        Assert.That(await _svc.ReplaceAsync("hi %test.gu%", new ReplacementContext(user: gu)),
            Is.EqualTo("hi Nick"));
    }

    [Test]
    public async Task ReplaceAsync_IDiscordClientParam_Works()
    {
        var store = new ReplacementPatternStore();
        var svc = new ReplacementService(store);
        await store.Register<DiscordSocketClient>("%test.client%", static c => c.ShardId.ToString());

        var client = Substitute.For<DiscordSocketClient>();
        var ctx = new ReplacementContext(client: client);
        var result = await svc.ReplaceAsync("shard %test.client%", ctx);
        Assert.That(result, Is.EqualTo("shard 0"));
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

    [Test]
    public async Task ReplaceAsync_TwoArgReplacement_FallbackInvokerWorks()
    {
        await _store.Register<IGuild, IUser>("%test.both%", static (g, u) => $"{g.Name}:{u.Username}");

        var guild = Substitute.For<IGuild>();
        guild.Name.Returns("G");
        var user = Substitute.For<IUser>();
        user.Username.Returns("U");

        var ctx = new ReplacementContext(guild: guild, user: user);
        var result = await _svc.ReplaceAsync("x %test.both% y", ctx);
        Assert.That(result, Is.EqualTo("x G:U y"));
    }
}
