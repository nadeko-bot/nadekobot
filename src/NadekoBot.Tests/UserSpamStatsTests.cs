using System.Collections.Generic;
using Discord;
using NadekoBot.Modules.Administration;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class UserSpamStatsTests
{
    private static IUserMessage CreateMessage(string content, params (string Filename, int Size)[] attachments)
    {
        var msg = Substitute.For<IUserMessage>();
        msg.Content.Returns(content);

        var attaches = new List<IAttachment>();
        foreach (var (filename, size) in attachments)
        {
            var att = Substitute.For<IAttachment>();
            att.Filename.Returns(filename);
            att.Size.Returns(size);
            attaches.Add(att);
        }

        msg.Attachments.Returns(attaches);
        return msg;
    }

    [Test]
    public void RepeatedTextOrAttachments_RaiseTheCount_AndADifferentAttachmentResetsIt()
    {
        var text = new UserSpamStats(CreateMessage("spam"));
        text.ApplyNextMessage(CreateMessage("spam"));
        text.ApplyNextMessage(CreateMessage("spam"));
        Assert.That(text.Count, Is.EqualTo(3));

        var image = new UserSpamStats(CreateMessage("", ("image.png", 1024)));
        image.ApplyNextMessage(CreateMessage("", ("image.png", 1024)));
        image.ApplyNextMessage(CreateMessage("", ("image.png", 1024)));
        Assert.That(image.Count, Is.EqualTo(3));

        image.ApplyNextMessage(CreateMessage("", ("other.jpg", 2048)));
        Assert.That(image.Count, Is.EqualTo(1));
    }
}
