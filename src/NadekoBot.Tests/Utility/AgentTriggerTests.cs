#nullable enable
using System;
using System.Collections.Generic;
using Discord;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Modules.Utility.AiAgent;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class AgentTriggerTests
{
    private const ulong BOT_ID = 100;
    private const ulong MEMBER_ID = 200;
    private const ulong OTHER_ID = 201;
    private const ulong GUILD_ID = 300;
    private const double WINDOW_SECONDS = 120;

    [Test]
    public void PingedReply_StartsOnlyOnTrackedAgentMessages_WhichExpireAndHaveACap()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new AgentMessageTracker();
        var agentMsgId = SnowflakeUtils.ToSnowflake(now);
        var otherBotMsgId = agentMsgId + 1;
        var oldId = SnowflakeUtils.ToSnowflake(now - AgentMessageTracker.TrackDuration - TimeSpan.FromHours(1));
        tracker.Add(agentMsgId);
        tracker.Add(oldId);
        tracker.CleanExpired(now);

        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsPingedReplyToAgent(Reply(agentMsgId, BOT_ID, [BOT_ID]), BOT_ID), Is.True);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(agentMsgId, BOT_ID, [MEMBER_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(otherBotMsgId, BOT_ID, [BOT_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(agentMsgId, MEMBER_ID, [BOT_ID, MEMBER_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(NotAReply([BOT_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(oldId, BOT_ID, [BOT_ID]), BOT_ID), Is.False);
        });

        var full = new AgentMessageTracker();
        for (var i = 0; i < AgentMessageTracker.MAX_TRACKED; i++)
            full.Add(agentMsgId + (ulong)i);

        var overCap = agentMsgId + AgentMessageTracker.MAX_TRACKED;
        full.Add(overCap);

        Assert.That(full.IsPingedReplyToAgent(Reply(agentMsgId, BOT_ID, [BOT_ID]), BOT_ID), Is.True);
        Assert.That(full.IsPingedReplyToAgent(Reply(overCap, BOT_ID, [BOT_ID]), BOT_ID), Is.False);
    }

    [Test]
    public void Session_ButtonMovesToTheNewestAnswerAndGoesWhenTheSessionEnds_NameCountsOnlyAtTheStart()
    {
        var time = new FakeTimeProvider();
        var sessions = new ConversationWindowTracker(time);

        sessions.Open(MEMBER_ID, Button(1, 10));
        sessions.Open(MEMBER_ID, Button(1, 11));
        Assert.That(Drain(sessions), Is.EqualTo(new[] { Button(1, 10) }));

        time.Advance(TimeSpan.FromSeconds(100));
        Assert.That(sessions.Touch(MEMBER_ID, 1), Is.True);
        time.Advance(TimeSpan.FromSeconds(100));
        sessions.CloseExpired(WINDOW_SECONDS, static _ => false);
        Assert.That(sessions.IsActive(MEMBER_ID, 1, WINDOW_SECONDS, busy: false), Is.True);
        Assert.That(Drain(sessions), Is.Empty);

        time.Advance(TimeSpan.FromSeconds(21));
        sessions.CloseExpired(WINDOW_SECONDS, static _ => true);
        Assert.That(sessions.IsActive(MEMBER_ID, 1, WINDOW_SECONDS, busy: true), Is.True);
        Assert.That(Drain(sessions), Is.Empty);

        Assert.That(sessions.IsActive(MEMBER_ID, 1, WINDOW_SECONDS, busy: false), Is.False);
        Assert.That(sessions.Touch(MEMBER_ID, 1), Is.False);
        Assert.That(Drain(sessions), Is.EqualTo(new[] { Button(1, 11) }));

        sessions.Open(MEMBER_ID, Button(4, 40));
        time.Advance(TimeSpan.FromSeconds(121));
        sessions.CloseExpired(WINDOW_SECONDS, static _ => false);
        Assert.That(Drain(sessions), Is.EqualTo(new[] { Button(4, 40) }));

        sessions.Open(MEMBER_ID, Button(2, 20));
        sessions.Open(MEMBER_ID, Button(3, 30));
        sessions.Open(OTHER_ID, Button(3, 31));
        Assert.That(sessions.CloseAll(MEMBER_ID, keepMessageId: 30), Is.True);
        Assert.That(sessions.CloseAll(MEMBER_ID), Is.False);
        Assert.That(sessions.IsActive(OTHER_ID, 3, WINDOW_SECONDS, busy: false), Is.True);
        Assert.That(sessions.Close(OTHER_ID, 3), Is.True);
        Assert.That(sessions.Close(OTHER_ID, 3), Is.False);
        Assert.That(Drain(sessions), Is.EquivalentTo(new[] { Button(2, 20), Button(3, 31) }));

        Assert.Multiple(() =>
        {
            Assert.That(AiAgentService.AfterBotName("Nadeko, what is my balance?", "Nadeko", null, "NadekoBot"),
                Is.EqualTo("what is my balance?"));
            Assert.That(AiAgentService.AfterBotName("  nadeko: .roll d20", "Nadeko", null, "NadekoBot"), Is.EqualTo(".roll d20"));
            Assert.That(AiAgentService.AfterBotName("Nadeko Bot! hi", "Nadeko", "Nadeko Bot", null), Is.EqualTo("hi"));
            Assert.That(AiAgentService.AfterBotName("NadekoBot hi", "Nadeko", null, "NadekoBot"), Is.EqualTo("hi"));
            Assert.That(AiAgentService.AfterBotName("ナデコ、天気は？", null, "ナデコ", "NadekoBot"), Is.EqualTo("天気は？"));
            Assert.That(AiAgentService.AfterBotName("Nadeko's music lags", "Nadeko", null, null), Is.Null);
            Assert.That(AiAgentService.AfterBotName("Nadeko-chan hi", "Nadeko", null, null), Is.Null);
            Assert.That(AiAgentService.AfterBotName("Nadekos are cute", "Nadeko", null, null), Is.Null);
            Assert.That(AiAgentService.AfterBotName("I asked Nadeko", "Nadeko", null, null), Is.Null);
            Assert.That(AiAgentService.AfterBotName("Nadeko!", "Nadeko", null, null), Is.Null);
            Assert.That(AiAgentService.AfterBotName("Nadeko... roll a die", "Nadeko", null, null), Is.EqualTo("roll a die"));
            Assert.That(AiAgentService.AfterBotName("nadeko.bot/commands is down", "Nadeko", null, null), Is.Null);
            Assert.That(AiAgentService.AfterBotName("Nadeko/s hi", "Nadeko", null, null), Is.Null);
        });
    }

    private static SessionButton Button(ulong channelId, ulong messageId)
        => new(GUILD_ID, channelId, messageId);

    private static List<SessionButton> Drain(ConversationWindowTracker sessions)
    {
        var stale = new List<SessionButton>();
        while (sessions.Stale.TryRead(out var button))
            stale.Add(button);

        return stale;
    }

    private static IUserMessage Reply(ulong repliedId, ulong repliedAuthorId, ulong[] mentions)
    {
        var author = Substitute.For<IUser>();
        author.Id.Returns(repliedAuthorId);

        var replied = Substitute.For<IUserMessage>();
        replied.Id.Returns(repliedId);
        replied.Author.Returns(author);

        var msg = NotAReply(mentions);
        msg.ReferencedMessage.Returns(replied);
        return msg;
    }

    private static IUserMessage NotAReply(ulong[] mentions)
    {
        var msg = Substitute.For<IUserMessage>();
        msg.MentionedUserIds.Returns(mentions);
        msg.ReferencedMessage.Returns((IUserMessage?)null);
        return msg;
    }
}
