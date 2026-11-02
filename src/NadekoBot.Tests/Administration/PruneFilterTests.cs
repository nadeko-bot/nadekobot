using System;
using System.Collections.Generic;
using Discord;
using NadekoBot.Modules.Administration.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class PruneFilterTests
{
    private static IMessage Msg(bool bot, string content, int files = 0, bool ping = false, bool rolePing = false, bool everyone = false)
    {
        var author = Substitute.For<IUser>();
        author.IsBot.Returns(bot);

        var msg = Substitute.For<IMessage>();
        msg.Author.Returns(author);
        msg.Content.Returns(content);

        var attachments = new List<IAttachment>();
        for (var i = 0; i < files; i++)
            attachments.Add(Substitute.For<IAttachment>());

        msg.Attachments.Returns(attachments);
        msg.Embeds.Returns(Array.Empty<IEmbed>());
        msg.MentionedUserIds.Returns(ping ? new ulong[] { 5 } : Array.Empty<ulong>());
        msg.MentionedRoleIds.Returns(rolePing ? new ulong[] { 7 } : Array.Empty<ulong>());
        msg.MentionedEveryone.Returns(everyone);
        return msg;
    }

    [Test]
    public void Matches_RequiresEveryGivenFilter()
    {
        var botLink = Msg(true, "buy at https://scam.example");
        var humanLink = Msg(false, "see HTTPS://docs.example");
        var humanInvite = Msg(false, "join discord.gg/abcdef");
        var humanFile = Msg(false, "Free NITRO here", files: 1, ping: true);

        var bots = new PruneFilter(true, false, false, false, false, false, false, null);
        Assert.That(bots.Matches(botLink), Is.True);
        Assert.That(bots.Matches(humanLink), Is.False);

        var humanLinks = new PruneFilter(false, true, true, false, false, false, false, null);
        Assert.That(humanLinks.Matches(humanLink), Is.True);
        Assert.That(humanLinks.Matches(botLink), Is.False);
        Assert.That(humanLinks.Matches(humanFile), Is.False);

        var invites = new PruneFilter(false, false, false, false, false, true, false, null);
        Assert.That(invites.Matches(humanInvite), Is.True);
        Assert.That(invites.Matches(humanLink), Is.False);

        var fileContains = new PruneFilter(false, false, false, true, false, false, true, "free nitro");
        Assert.That(fileContains.Matches(humanFile), Is.True);
        Assert.That(fileContains.Matches(humanLink), Is.False);

        var pings = new PruneFilter(false, false, false, false, false, false, true, null);
        Assert.That(pings.Matches(Msg(false, "hey <@&7>", rolePing: true)), Is.True);
        Assert.That(pings.Matches(Msg(false, "@everyone hi", everyone: true)), Is.True);
        Assert.That(pings.Matches(humanLink), Is.False);

        var none = new PruneFilter(false, false, false, false, false, false, false, null);
        Assert.That(none.IsEmpty, Is.True);
        Assert.That(none.Matches(humanInvite), Is.True);
    }
}
