#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Common.Yml;
using NadekoBot.Modules.Owner.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;
using SelfCommands = NadekoBot.Modules.Owner.Owner.SelfCommands;

namespace NadekoBot.Tests.Owner;

public sealed class BotPresenceTests
{
    private const string TWITCH = "https://twitch.tv/nadeko";

    private TestDbService _db = null!;
    private DiscordSocketClient _client = null!;
    private BotConfigService _bss = null!;
    private BotActivityService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _client = new DiscordSocketClient();
        _bss = CreateConfig();
        _svc = CreateService(_db, _bss, _client);
    }

    [TearDown]
    public void TearDown()
    {
        ResetConfig(_bss);
        _client.Dispose();
        _db.Dispose();
    }

    // BotConfigService reads data/bot.yml in the test folder, so each test starts from the default presence.
    public static BotConfigService CreateConfig()
    {
        var bss = new BotConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        ResetConfig(bss);
        return bss;
    }

    public static void ResetConfig(BotConfigService bss)
        => bss.ModifyConfig(static c =>
        {
            c.Presence = new();
            c.RotateStatuses = false;
        });

    public static BotActivityService CreateService(
        DbService db,
        BotConfigService bss,
        DiscordSocketClient client,
        TaskCompletionSource<string?>? slow = null)
    {
        var rep = Substitute.For<IReplacementService>();
        rep.ReplaceAsync(Arg.Any<string>(), Arg.Any<ReplacementContext>())
           .Returns(ci =>
           {
               var input = ci.ArgAt<string>(0);
               return slow is not null && input.Contains("%slow%")
                   ? new ValueTask<string?>(slow.Task)
                   : new ValueTask<string?>(input.Replace("%bot.name%", "Nadeko"));
           });

        return new BotActivityService(new EventPubSub(), client, db, rep, bss);
    }

    [Test]
    public void Rules_AcceptOnlyTheStatusesTypesTextsAndStreamLinksWhichDiscordShows()
    {
        UserStatus[] settable = [UserStatus.Online, UserStatus.Idle, UserStatus.DoNotDisturb, UserStatus.Invisible];
        Assert.That(settable.All(BotPresence.IsSettableStatus), Is.True);
        Assert.That(BotPresence.IsSettableStatus(UserStatus.Offline), Is.False);
        Assert.That(BotPresence.IsSettableStatus(UserStatus.AFK), Is.False);

        var longest = new string('a', BotPresence.MAX_TEXT_LENGTH);
        (ActivityType Type, string? Text, string? Url, PresenceError Expected)[] cases =
        [
            (ActivityType.CustomStatus, longest, null, PresenceError.None),
            (ActivityType.Streaming, "live", TWITCH, PresenceError.None),
            (ActivityType.Streaming, "live", "https://www.youtube.com/watch?v=abc", PresenceError.None),
            (ActivityType.Playing, "   ", null, PresenceError.Text),
            (ActivityType.Playing, null, null, PresenceError.Text),
            (ActivityType.Playing, longest + "a", null, PresenceError.Text),
            ((ActivityType)99, "x", null, PresenceError.Type),
            (ActivityType.Streaming, "live", "https://example.com/live", PresenceError.Url),
            (ActivityType.Streaming, "live", "http://twitch.tv/nadeko", PresenceError.Url),
            (ActivityType.Streaming, "live", "https://twitch.tv/", PresenceError.Url),
            (ActivityType.Streaming, "live", null, PresenceError.Url),
            (ActivityType.Playing, "x", TWITCH, PresenceError.Url),
        ];

        foreach (var (type, text, url, expected) in cases)
            Assert.That(BotPresence.TryCreateActivity(type, text, url, out _), Is.EqualTo(expected), $"{type} '{text}' {url}");

        BotPresence.TryCreateActivity(ActivityType.Watching, "  anime  ", null, out var trimmed);
        Assert.That(trimmed?.Text, Is.EqualTo("anime"));

        var edited = BotPresence.Normalize(new PresenceConfig
        {
            Status = UserStatus.AFK,
            Activity = new() { Type = ActivityType.Playing, Text = "" }
        });
        Assert.That(edited, Is.EqualTo((UserStatus.Online, (PresenceActivity?)null)));
    }

    [Test]
    public async Task SetStatusCommand_SavesEachChoiceAsTheStatusWhichDiscordShows()
    {
        (SelfCommands.SettableUserStatus Choice, UserStatus Expected)[] choices =
        [
            (SelfCommands.SettableUserStatus.Online, UserStatus.Online),
            (SelfCommands.SettableUserStatus.Idle, UserStatus.Idle),
            (SelfCommands.SettableUserStatus.Dnd, UserStatus.DoNotDisturb),
            (SelfCommands.SettableUserStatus.Invisible, UserStatus.Invisible)
        ];
        Assert.That(choices.Select(static c => c.Choice), Is.EquivalentTo(Enum.GetValues<SelfCommands.SettableUserStatus>()));

        foreach (var (choice, expected) in choices)
        {
            Assert.That(await _svc.SetStatusAsync(SelfCommands.SettableUserStatusToUserStatus(choice)),
                Is.EqualTo(PresenceError.None),
                choice.ToString());
            Assert.That(_svc.GetPresence().Status, Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task SetPresence_SavesTheStatusStopsTheRotationAndKeepsItAfterARestart()
    {
        await _svc.AddPlaying(ActivityType.Playing, "rotating");
        Assert.That(await _svc.ToggleRotatePlayingAsync(), Is.True);

        Assert.That(await _svc.SetPresenceAsync(UserStatus.Idle, ActivityType.Watching, "  anime  ", null),
            Is.EqualTo(PresenceError.None));

        AssertPresence(_svc.GetPresence(), UserStatus.Idle, ActivityType.Watching, "anime", null, rotating: false);
        Assert.That(_svc.GetRotatingStatuses(), Has.Count.EqualTo(1));

        var restarted = new BotConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        AssertPresence(CreateService(_db, restarted, _client).GetPresence(),
            UserStatus.Idle,
            ActivityType.Watching,
            "anime",
            null,
            rotating: false);

        Assert.That(await _svc.SetPresenceAsync(UserStatus.Online, ActivityType.Streaming, "live", "https://example.com/live"),
            Is.EqualTo(PresenceError.Url));
        Assert.That(await _svc.SetPresenceAsync(UserStatus.Offline, null, null, null), Is.EqualTo(PresenceError.Status));
        AssertPresence(_svc.GetPresence(), UserStatus.Idle, ActivityType.Watching, "anime", null, rotating: false);

        Assert.That(await _svc.SetPresenceAsync(UserStatus.Online, null, null, null), Is.EqualTo(PresenceError.None));
        Assert.That(_svc.GetPresence(), Is.EqualTo(new BotPresenceState(UserStatus.Online, null, false)));
    }

    [Test]
    public async Task Commands_ChangeOnlyTheirPartOfTheSavedPresence()
    {
        Assert.That(await _svc.SetActivityAsync(ActivityType.Watching, "anime"), Is.EqualTo(PresenceError.None));
        AssertPresence(_svc.GetPresence(), UserStatus.Online, ActivityType.Watching, "anime", null, rotating: false);

        Assert.That(await _svc.ToggleRotatePlayingAsync(), Is.True);
        Assert.That(await _svc.SetStatusAsync(UserStatus.DoNotDisturb), Is.EqualTo(PresenceError.None));
        AssertPresence(_svc.GetPresence(), UserStatus.DoNotDisturb, ActivityType.Watching, "anime", null, rotating: true);

        Assert.That(await _svc.SetStreamAsync(TWITCH, "live"), Is.EqualTo(PresenceError.None));
        AssertPresence(_svc.GetPresence(), UserStatus.DoNotDisturb, ActivityType.Streaming, "live", TWITCH, rotating: false);

        Assert.That(await _svc.SetStreamAsync("https://example.com/live", "live"), Is.EqualTo(PresenceError.Url));
        Assert.That(await _svc.SetActivityAsync(null, new string('a', BotPresence.MAX_TEXT_LENGTH + 1)),
            Is.EqualTo(PresenceError.Text));
        AssertPresence(_svc.GetPresence(), UserStatus.DoNotDisturb, ActivityType.Streaming, "live", TWITCH, rotating: false);

        Assert.That(await _svc.SetActivityAsync(null, "hi"), Is.EqualTo(PresenceError.None));
        AssertPresence(_svc.GetPresence(), UserStatus.DoNotDisturb, ActivityType.CustomStatus, "hi", null, rotating: false);

        Assert.That(await _svc.SetActivityAsync(ActivityType.Playing, null), Is.EqualTo(PresenceError.None));
        Assert.That(_svc.GetPresence().Activity, Is.Null);
    }

    [Test]
    public async Task ApplySaved_ShowsTheSavedPresenceWithBotPlaceholdersOnTheShard()
    {
        await _svc.SetPresenceAsync(UserStatus.Invisible, ActivityType.Streaming, "%bot.name% live", TWITCH);
        await _svc.ApplySavedAsync();

        Assert.That(_client.Status, Is.EqualTo(UserStatus.Invisible));
        Assert.That(_client.Activity, Is.TypeOf<StreamingGame>());
        Assert.That(((StreamingGame)_client.Activity).Name, Is.EqualTo("Nadeko live"));
        Assert.That(((StreamingGame)_client.Activity).Url, Is.EqualTo(TWITCH));

        // A gamestatus event shows its code, and the saved activity comes back when it ends.
        await _client.SetGameAsync("code");
        await _svc.ApplySavedAsync();
        Assert.That(_client.Activity, Is.TypeOf<StreamingGame>());

        await _svc.SetActivityAsync(null, "hi");
        await _svc.ApplySavedAsync();
        Assert.That(_client.Activity, Is.TypeOf<CustomStatusGame>());
        Assert.That(((CustomStatusGame)_client.Activity).State, Is.EqualTo("hi"));
    }

    [Test]
    public async Task Apply_AnOlderActivityWhichFinishesLastDoesNotReplaceANewerOne()
    {
        var slow = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var svc = CreateService(_db, _bss, _client, slow);

        await svc.SetActivityAsync(ActivityType.Playing, "%slow%");
        var older = svc.ApplySavedAsync();

        await svc.SetActivityAsync(ActivityType.Watching, "anime");
        await svc.ApplySavedAsync();

        slow.SetResult("old");
        await older;

        Assert.That(_client.Activity?.Type, Is.EqualTo(ActivityType.Watching));
        Assert.That(_client.Activity?.Name, Is.EqualTo("anime"));
    }

    private static void AssertPresence(
        BotPresenceState state,
        UserStatus status,
        ActivityType type,
        string text,
        string? url,
        bool rotating)
    {
        Assert.That(state.Status, Is.EqualTo(status));
        Assert.That(state.Activity, Is.Not.Null);
        Assert.That((state.Activity!.Type, state.Activity.Text, state.Activity.Url), Is.EqualTo((type, text, url)));
        Assert.That(state.Rotating, Is.EqualTo(rotating));
    }
}
