#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using NadekoBot.Common;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Modules.Utility.AiAgent.Adapters;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class AiMessagingToolTests
{
    private const ulong CHANNEL_ID = 10;
    private const ulong BOT_ID = 20;
    private const ulong MESSAGE_ID = 30;
    private const ulong USER_ID = 40;
    private const ulong OWN_MESSAGE_ID = 50;
    private const ulong SYSTEM_MESSAGE_ID = 60;

    [Test]
    public async Task MessageTools_CheckTheMemberAndTheBot()
    {
        var sender = Substitute.For<IMessageSenderService>();
        var adapter = CreateAdapter(sender);
        var member = CreateContext(manageMessages: false);

        // A refused action makes no Discord call.
        var editDenied = Assert.ThrowsAsync<ToolException>(() => adapter.EditMessage(member, CHANNEL_ID, MESSAGE_ID, "x"));
        Assert.That(editDenied!.Code, Is.EqualTo("missing_permission"));
        _ = member.SourceChannel.DidNotReceiveWithAnyArgs().GetMessageAsync(default, default, default);
        var deleteDenied = Assert.ThrowsAsync<ToolException>(() => adapter.DeleteMessage(member, CHANNEL_ID, MESSAGE_ID));
        Assert.That(deleteDenied!.Code, Is.EqualTo("missing_permission"));
        _ = member.SourceChannel.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default(IMessage)!, default);

        member.SentMessageIds[MESSAGE_ID] = true;
        Assert.That(await adapter.EditMessage(member, CHANNEL_ID, MESSAGE_ID, "x"), Is.EqualTo("Message edited successfully."));
        Assert.That(await adapter.DeleteMessage(member, CHANNEL_ID, MESSAGE_ID), Is.EqualTo("Message deleted successfully."));

        var moderator = CreateContext(manageMessages: true);
        Assert.That(await adapter.EditMessage(moderator, CHANNEL_ID, MESSAGE_ID, "x"), Is.EqualTo("Message edited successfully."));
        Assert.That(await adapter.DeleteMessage(moderator, CHANNEL_ID, MESSAGE_ID), Is.EqualTo("Message deleted successfully."));

        // A member deletes their own message without Manage Messages, but the bot needs it for a message of a member.
        adapter = CreateAdapter(sender);
        Assert.That(await adapter.DeleteMessage(CreateContext(manageMessages: false), CHANNEL_ID, OWN_MESSAGE_ID),
            Is.EqualTo("Message deleted successfully."));
        var botDenied = Assert.ThrowsAsync<ToolException>(() =>
            adapter.DeleteMessage(CreateContext(manageMessages: false, botManageMessages: false), CHANNEL_ID, OWN_MESSAGE_ID));
        Assert.That(botDenied!.Code, Is.EqualTo("bot_missing_permission"));

        // A join or pin notice has the member as its author, but it is not their message.
        var systemDenied = Assert.ThrowsAsync<ToolException>(() =>
            adapter.DeleteMessage(CreateContext(manageMessages: false), CHANNEL_ID, SYSTEM_MESSAGE_ID));
        Assert.That(systemDenied!.Code, Is.EqualTo("missing_permission"));

        // A hidden channel does not exist for the member, and reading needs the history for both.
        var hidden = Assert.ThrowsAsync<ToolException>(() =>
            adapter.GetMessage(CreateContext(manageMessages: false, memberView: false), CHANNEL_ID, MESSAGE_ID));
        Assert.That(hidden!.Message, Is.EqualTo(AgentPermissions.ChannelNotFound().Message));
        var noHistory = Assert.ThrowsAsync<ToolException>(() =>
            adapter.GetMessage(CreateContext(manageMessages: false, memberHistory: false), CHANNEL_ID, MESSAGE_ID));
        Assert.That(noHistory!.Code, Is.EqualTo("missing_permission"));
        var botBlind = Assert.ThrowsAsync<ToolException>(() =>
            adapter.GetMessage(CreateContext(manageMessages: false, botHistory: false), CHANNEL_ID, MESSAGE_ID));
        Assert.That(botBlind!.Code, Is.EqualTo("bot_missing_permission"));

        var slowDenied = Assert.ThrowsAsync<ToolException>(() =>
            adapter.SendMessage(CreateContext(manageMessages: false, slowMode: true), CHANNEL_ID.ToString(), "hi"));
        Assert.That(slowDenied!.Code, Is.EqualTo("missing_permission"));
        var tooLong = Assert.ThrowsAsync<ToolException>(() =>
            adapter.SendMessage(CreateContext(manageMessages: false), CHANNEL_ID.ToString(), new string('a', 9000)));
        Assert.That(tooLong!.Code, Is.EqualTo("invalid_argument"));
        sender.DidNotReceiveWithAnyArgs().Response(default(IMessageChannel)!);

        // A thread uses the permissions for threads. A locked or archived thread, a bot outside a private thread and a bot with a timeout refuse.
        var inThreads = new ChannelPermissions(viewChannel: true, readMessageHistory: true, sendMessagesInThreads: true);
        var thread = new AgentChannelAccess(inThreads, inThreads, true, false, false, false, false);
        Assert.That(AgentPermissions.CheckSend(thread, false, false), Is.Null);
        var channelOnly = new ChannelPermissions(viewChannel: true, sendMessages: true, readMessageHistory: true);
        Assert.That(AgentPermissions.CheckSend(thread with { Member = channelOnly }, false, false)!.Code, Is.EqualTo("missing_permission"));
        Assert.That(AgentPermissions.CheckSend(thread with { Locked = true }, false, false)!.Code, Is.EqualTo("missing_permission"));
        Assert.That(AgentPermissions.CheckEdit(thread with { Archived = true }, true)!.Code, Is.EqualTo("forbidden"));
        Assert.That(AgentPermissions.CheckSend(thread with { BotOutside = true }, false, false)!.Code, Is.EqualTo("bot_missing_permission"));
        Assert.That(AgentPermissions.CheckFind(thread with { BotTimedOut = true }, true)!.Code, Is.EqualTo("bot_missing_permission"));

        // The question and its options fit one message with the ping, so the answer request sees what the member saw.
        var options = new List<string> { " " };
        for (var i = 0; i < 12; i++)
            options.Add(new string((char)('a' + i), 200));
        var question = SessionAiAdapter.BuildQuestion(new string('q', 1500), options);
        Assert.That(question.Length + $"<@{ulong.MaxValue}>\n".Length, Is.LessThanOrEqualTo(DiscordConfig.MaxMessageSize));
        Assert.That(question, Does.Contain("**1.** " + new string('a', 80) + "...").And.Contain("**10.** " + new string('j', 80) + "...")
            .And.Not.Contain("**11.**"));
        Assert.That(SessionAiAdapter.BuildQuestion("Which one?", [" "]), Is.EqualTo("Which one?"));
    }

    private static MessagingAiAdapter CreateAdapter(IMessageSenderService sender)
        => new(sender, IntentState.All, new AgentLimiter());

    private static AiToolContext CreateContext(
        bool manageMessages,
        bool botManageMessages = true,
        bool slowMode = false,
        bool memberView = true,
        bool memberHistory = true,
        bool botHistory = true)
    {
        var bot = Substitute.For<IGuildUser>();
        bot.Id.Returns(BOT_ID);

        var user = Substitute.For<IGuildUser>();
        user.Id.Returns(USER_ID);

        var msg = Substitute.For<IUserMessage>();
        msg.Id.Returns(MESSAGE_ID);
        msg.Author.Returns(bot);
        msg.Source.Returns(MessageSource.Bot);

        var own = Substitute.For<IUserMessage>();
        own.Id.Returns(OWN_MESSAGE_ID);
        own.Author.Returns(user);
        own.Source.Returns(MessageSource.User);

        var system = Substitute.For<IUserMessage>();
        system.Id.Returns(SYSTEM_MESSAGE_ID);
        system.Author.Returns(user);
        system.Source.Returns(MessageSource.System);

        var channel = Substitute.For<ITextChannel>();
        channel.Id.Returns(CHANNEL_ID);
        channel.SlowModeInterval.Returns(slowMode ? 30 : 0);
        channel.GetMessageAsync(MESSAGE_ID, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(msg);
        channel.GetMessageAsync(OWN_MESSAGE_ID, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(own);
        channel.GetMessageAsync(SYSTEM_MESSAGE_ID, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(system);

        var guild = Substitute.For<IGuild>();
        guild.GetTextChannelAsync(CHANNEL_ID, Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(channel);
        guild.GetCurrentUserAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(bot);

        bot.GetPermissions(channel)
           .Returns(new ChannelPermissions(viewChannel: true,
               sendMessages: true,
               readMessageHistory: botHistory,
               manageMessages: botManageMessages));

        user.GetPermissions(channel)
            .Returns(new ChannelPermissions(viewChannel: memberView,
                sendMessages: true,
                readMessageHistory: memberHistory,
                manageMessages: manageMessages));

        return new AiToolContext
        {
            Guild = guild,
            SourceChannel = channel,
            User = user,
            TriggerMessage = Substitute.For<IUserMessage>(),
            CancellationToken = CancellationToken.None
        };
    }
}
