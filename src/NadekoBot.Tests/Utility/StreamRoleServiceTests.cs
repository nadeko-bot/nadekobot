using Discord;
using NadekoBot.Modules.Utility.Services;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class StreamRoleServiceTests
{
    [Test]
    public void StreamChanged_DetectsStreamStartStopAndChange()
    {
        IActivity[] game = [new Game("Some Game")];
        IActivity[] stream = [new StreamingGame("My stream", "https://twitch.tv/me")];
        IActivity[] otherTitle = [new StreamingGame("Another title", "https://twitch.tv/me")];

        Assert.That(StreamRoleService.StreamChanged(game, stream), Is.True);
        Assert.That(StreamRoleService.StreamChanged(stream, game), Is.True);
        Assert.That(StreamRoleService.StreamChanged(null, stream), Is.True);
        Assert.That(StreamRoleService.StreamChanged(stream, otherTitle), Is.True);

        Assert.That(StreamRoleService.StreamChanged(stream, [new StreamingGame("My stream", "https://twitch.tv/me")]),
            Is.False);
        Assert.That(StreamRoleService.StreamChanged(game, [new Game("Other Game"), new Game("Third")]), Is.False);
    }
}
