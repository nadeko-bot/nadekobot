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
    public async Task Store_PrecomputesSlotsAndFiltersByContextMask()
    {
        var noParam = new ReplacementInfo("%time%", static () => new ValueTask<string>("now"));
        Assert.That(noParam.RequiredMask, Is.EqualTo(ContextMask.None));
        Assert.That(noParam.ParamSlotIndices, Is.Empty);

        var guildAndUser = new ReplacementInfo("%both%",
            static (IGuild g, IUser u) => new ValueTask<string>($"{g.Name}:{u.Username}"));
        Assert.That(guildAndUser.RequiredMask, Is.EqualTo(ContextMask.Guild | ContextMask.User));
        Assert.That(guildAndUser.ParamSlotIndices, Is.EqualTo(new[] { ContextSlot.Guild, ContextSlot.User }));

        await _store.Register("%test.static%", static () => "now");
        await _store.Register<IUser>("%test.user%", static u => u.Username);

        var noContext = _store.GetReplacementsForMask(ContextMask.None);
        Assert.That(noContext.Any(r => r.Token == "%test.static%"), Is.True);
        Assert.That(noContext.Any(r => r.Token == "%test.user%"), Is.False);
        Assert.That(_store.GetReplacementsForMask(ContextMask.None), Is.SameAs(noContext));

        var withUser = _store.GetReplacementsForMask(ContextMask.User);
        Assert.That(withUser.Any(r => r.Token == "%test.static%"), Is.True);
        Assert.That(withUser.Any(r => r.Token == "%test.user%"), Is.True);

        Assert.That(_store.GetReplacementsForMask(ContextMask.Guild).Any(r => r.Token == "%test.user%"), Is.False);

        await _store.Register("%test.new%", static () => "today");
        Assert.That(_store.GetReplacementsForMask(ContextMask.None), Has.Length.EqualTo(noContext.Length + 1));
    }

    [Test]
    public async Task ReplaceAsync_ReplacesKnownTokensAndLeavesTheRestAlone()
    {
        await _store.Register("%time%", static () => "now");
        await _store.Register("%test.a%", static () => "x");
        await _store.Register("%test.b%", static () => "y");
        await _store.Register("%test.k%", static () => "V");
        await _store.Register("%name%", static () => "base");
        await _store.Register<IUser>("%user%", static u => u.Username);
        await _store.Register(new Regex(@"%test\.echo-(\w+)%"), static m => m.Groups[1].Value);

        var ctx = new ReplacementContext();
        (string Input, string Expected)[] cases =
        [
            ("it is %time%", "it is now"),
            ("%test.a% and %test.b%", "x and y"),
            ("prefix %test.a% middle %test.b% suffix", "prefix x middle y suffix"),
            ("%%%test.k%", "%%V"),
            ("say %test.echo-hello%", "say hello"),
            ("hi %unknown%", "hi %unknown%"),
            ("hi %user%", "hi %user%"),
            ("hi %foo", "hi %foo"),
        ];

        foreach (var (input, expected) in cases)
            Assert.That(await _svc.ReplaceAsync(input, ctx), Is.EqualTo(expected), input);

        var overridden = new ReplacementContext().WithOverride("%name%", () => "override");
        Assert.That(await _svc.ReplaceAsync("hi %name%", overridden), Is.EqualTo("hi override"));
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
    public async Task ReplaceAsync_PassesTheContextObjectsToEachParameter()
    {
        await _store.Register<IUser>("%test.uname%", static u => u.Username);
        await _store.Register<IGuild>("%test.guild%", static g => g.Name);
        await _store.Register<IMessageChannel>("%test.ch%", static ch => ch.Name);
        await _store.Register<IGuildUser>("%test.gu%", static gu => gu.DisplayName);
        await _store.Register<DiscordSocketClient>("%test.client%", static c => c.ShardId.ToString());
        await _store.Register<IGuild, IUser>("%test.both%", static (g, u) => $"{g.Name}:{u.Username}");

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

        var client = Substitute.For<DiscordSocketClient>();
        Assert.That(await _svc.ReplaceAsync("shard %test.client%", new ReplacementContext(client: client)),
            Is.EqualTo("shard 0"));

        Assert.That(await _svc.ReplaceAsync("x %test.both% y", new ReplacementContext(guild: guild, user: user)),
            Is.EqualTo("x TestGuild:TestUser y"));
    }
}
