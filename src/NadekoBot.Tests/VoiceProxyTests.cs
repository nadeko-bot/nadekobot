#nullable enable
using System.Threading.Tasks;
using NadekoBot.Modules.Music;
using NadekoBot.Voice;
using NadekoBot.Voice.Models;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class VoiceProxyTests
{
    private const ulong GUILD_ID = 1;
    private const ulong CHANNEL_ID = 2;
    private const ulong USER_ID = 3;

    private static VoiceGateway NewGateway(ulong channelId = CHANNEL_ID, string token = "token", string endpoint = "voice.example:443")
        => new(GUILD_ID, channelId, USER_ID, "session", token, endpoint);

    [Test]
    public async Task Gateway_MatchesOnlyItsEvent_AndGetsTheSpeakingState()
    {
        var matcher = new VoiceProxy(null!);
        Assert.That(matcher.IsGatewayFor(CHANNEL_ID, "token", "voice.example:443"), Is.False);

        matcher.SetGateway(NewGateway());
        Assert.That(matcher.IsGatewayFor(CHANNEL_ID, "token", "voice.example:443"), Is.True);
        Assert.That(matcher.IsGatewayFor(5, "token", "voice.example:443"), Is.False);
        Assert.That(matcher.IsGatewayFor(CHANNEL_ID, "new-token", "voice.example:443"), Is.False);
        Assert.That(matcher.IsGatewayFor(CHANNEL_ID, "token", "other.example:443"), Is.False);
        matcher.SetGateway(null!);

        var proxy = new VoiceProxy(null!);

        await proxy.StartSpeakingAsync();

        var first = NewGateway();
        proxy.SetGateway(first);
        Assert.That(first.Speaking, Is.EqualTo(VoiceSpeaking.State.Microphone));

        var moved = NewGateway(channelId: 5);
        proxy.SetGateway(moved);
        Assert.That(moved.Speaking, Is.EqualTo(VoiceSpeaking.State.Microphone));

        var stop = proxy.StopSpeakingAsync();
        Assert.That(moved.Speaking, Is.EqualTo(VoiceSpeaking.State.None));

        proxy.SetGateway(null!);
        await stop;

        var next = NewGateway();
        proxy.SetGateway(next);
        Assert.That(next.Speaking, Is.EqualTo(VoiceSpeaking.State.None));
        proxy.SetGateway(null!);
    }
}
