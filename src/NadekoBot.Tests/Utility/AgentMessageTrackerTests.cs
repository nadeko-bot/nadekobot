#nullable enable
using System;
using Discord;
using NadekoBot.Modules.Utility.AiAgent;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class AgentMessageTrackerTests
{
    private const ulong BOT_ID = 100;
    private const ulong MEMBER_ID = 200;

    [Test]
    public void IsPingedReplyToAgent_OnlyForPingedRepliesToAgentMessages()
    {
        var tracker = new AgentMessageTracker();
        var agentMsgId = SnowflakeUtils.ToSnowflake(DateTimeOffset.UtcNow);
        var otherBotMsgId = agentMsgId + 1;
        tracker.Add(agentMsgId);

        Assert.Multiple(() =>
        {
            Assert.That(tracker.IsPingedReplyToAgent(Reply(agentMsgId, BOT_ID, [BOT_ID]), BOT_ID), Is.True);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(agentMsgId, BOT_ID, [MEMBER_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(otherBotMsgId, BOT_ID, [BOT_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(Reply(agentMsgId, MEMBER_ID, [BOT_ID, MEMBER_ID]), BOT_ID), Is.False);
            Assert.That(tracker.IsPingedReplyToAgent(NotAReply([BOT_ID]), BOT_ID), Is.False);
        });
    }

    [Test]
    public void CleanExpired_RemovesOldIds_AndAddStopsAtCap()
    {
        var now = DateTimeOffset.UtcNow;
        var tracker = new AgentMessageTracker();
        var oldId = SnowflakeUtils.ToSnowflake(now - AgentMessageTracker.TrackDuration - TimeSpan.FromHours(1));
        var freshId = SnowflakeUtils.ToSnowflake(now);
        tracker.Add(oldId);
        tracker.Add(freshId);

        tracker.CleanExpired(now);

        Assert.That(tracker.IsPingedReplyToAgent(Reply(oldId, BOT_ID, [BOT_ID]), BOT_ID), Is.False);
        Assert.That(tracker.IsPingedReplyToAgent(Reply(freshId, BOT_ID, [BOT_ID]), BOT_ID), Is.True);

        var full = new AgentMessageTracker();
        for (var i = 0; i < AgentMessageTracker.MAX_TRACKED; i++)
            full.Add(freshId + (ulong)i);

        var overCap = freshId + AgentMessageTracker.MAX_TRACKED;
        full.Add(overCap);

        Assert.That(full.IsPingedReplyToAgent(Reply(freshId, BOT_ID, [BOT_ID]), BOT_ID), Is.True);
        Assert.That(full.IsPingedReplyToAgent(Reply(overCap, BOT_ID, [BOT_ID]), BOT_ID), Is.False);
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
