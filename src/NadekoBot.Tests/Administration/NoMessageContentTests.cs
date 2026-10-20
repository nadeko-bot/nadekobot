#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

// Without Message Content every member message arrives with empty text.
public sealed class NoMessageContentTests
{
    private const ulong GUILD = 1;
    private const ulong CHANNEL = 2;
    private const int THRESHOLD = 3;
    private const int BURST = 10;

    private static readonly IntentState _noMessageContent = new(true, true, false);

    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private static IUserMessage EmptyMessage(ulong authorId)
    {
        var channel = Substitute.For<ITextChannel>();
        channel.Id.Returns(CHANNEL);
        channel.GuildId.Returns(GUILD);
        channel.Guild.Id.Returns(GUILD);

        var author = Substitute.For<IGuildUser>();
        author.Id.Returns(authorId);
        author.IsBot.Returns(false);

        var msg = Substitute.For<IUserMessage>();
        msg.Content.Returns(string.Empty);
        msg.Attachments.Returns(new List<IAttachment>());
        msg.Channel.Returns(channel);
        msg.Author.Returns(author);
        return msg;
    }

    private async Task<bool> SpamPunishesAsync(IntentState intents)
    {
        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        var svc = new ProtectionService(client,
            null!,
            _db,
            null!,
            Substitute.For<INotifySubscriber>(),
            new ShardData(client, creds),
            intents);

        var punished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.OnAntiProtectionTriggered += (_, type, _) =>
        {
            if (type == ProtectionType.Spamming)
                punished.TrySetResult(true);
            return Task.CompletedTask;
        };

        await svc.StartAntiSpamAsync(GUILD, THRESHOLD, PunishmentAction.Kick, 0, null);

        for (var i = 0; i < BURST; i++)
            await svc.HandleAntiSpam(EmptyMessage(authorId: 100));

        return await Task.WhenAny(punished.Task, Task.Delay(TimeSpan.FromSeconds(2))) == punished.Task;
    }

    [Test]
    public async Task AntiSpam_DoesNotPunishEmptyMessagesWithoutMessageContent()
    {
        Assert.That(await SpamPunishesAsync(_noMessageContent), Is.False);
        Assert.That(await SpamPunishesAsync(IntentState.All), Is.True);

        await using var ctx = _db.GetDbContext();
        Assert.That(await ctx.GetTable<AntiSpamSetting>().CountAsyncLinqToDB(x => x.GuildId == GUILD), Is.EqualTo(1));
    }

    [Test]
    public async Task ImageOnlyChannel_KeepsMessagesAndSettingWithoutMessageContent()
    {
        var svc = new SomethingOnlyChannelService(new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<DiscordSocketClient>(),
            _db,
            _noMessageContent);
        await svc.ToggleAsync(GUILD, CHANNEL, OnlyChannelType.Image);

        var msg = EmptyMessage(authorId: 100);
        Assert.That(await svc.ExecOnMessageAsync(null, msg), Is.False);
        Assert.That(svc.GetMode(GUILD, CHANNEL), Is.EqualTo(OnlyChannelType.Image));
        await msg.DidNotReceiveWithAnyArgs().DeleteAsync();
    }
}
