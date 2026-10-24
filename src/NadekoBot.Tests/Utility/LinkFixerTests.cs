using System;
using System.Collections.Generic;
using Discord;
using NadekoBot.Modules.Utility.LinkFixer;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class LinkFixerTests
{
    [Test]
    public void BuildReply_FixesLinksInOneShortReplyAndIgnoresCase()
    {
        var fixes = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            ["x.com"] = "fxtwitter.com"
        };

        // a link which would make the reply longer than a discord message is left out
        var content = $"https://x.com/{new string('a', DiscordConfig.MaxMessageSize)} "
                      + "see https://x.com/1 https://other.com/2\nhttps://www.X.COM/3";
        for (var i = 4; i < 4 + LinkFixerService.MAX_FIXED_LINKS; i++)
            content += $" https://x.com/{i}";

        var lines = LinkFixerService.BuildReply(content, fixes)!.Split('\n');

        Assert.That(lines, Has.Length.EqualTo(LinkFixerService.MAX_FIXED_LINKS));
        Assert.That(lines[0], Is.EqualTo("https://fxtwitter.com/1"));
        Assert.That(lines[1], Is.EqualTo("https://fxtwitter.com/3"));

        Assert.That(LinkFixerService.BuildReply("no fix for https://other.com/a", fixes), Is.Null);
        Assert.That(LinkFixerService.BuildReply(null, fixes), Is.Null);
    }
}
