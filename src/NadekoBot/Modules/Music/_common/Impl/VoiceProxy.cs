#nullable disable
using NadekoBot.Voice;
using NadekoBot.Voice.Models;

namespace NadekoBot.Modules.Music;

public sealed class VoiceProxy : IVoiceProxy
{
    public enum VoiceProxyState
    {
        Created,
        Started,
        Stopped
    }

    private const int MAX_ERROR_COUNT = 20;
    private const int DELAY_ON_ERROR_MILISECONDS = 200;

    public VoiceProxyState State
        => Volatile.Read(ref gateway) switch
        {
            { Started: true, Stopped: false } => VoiceProxyState.Started,
            { Stopped: false } => VoiceProxyState.Created,
            _ => VoiceProxyState.Stopped
        };

    public bool IsReady
        => Volatile.Read(ref gateway) is { CanSendAudio: true };

    private VoiceGateway gateway;

    // The wanted speaking state outlives a gateway, because a join or a move replaces the gateway during a track.
    private int _speaking;
    private readonly Func<VoiceGateway, Task> _sendSpeaking;

    public VoiceProxy(VoiceGateway initial)
    {
        gateway = initial;
        _sendSpeaking = gw => gw.SendSpeakingAsync((VoiceSpeaking.State)Volatile.Read(ref _speaking));
    }

    public bool SendPcmFrame(VoiceClient vc, Span<byte> data, int length)
    {
        try
        {
            var gw = Volatile.Read(ref gateway);
            if (gw is null || gw.Stopped || !gw.Started)
            {
                Log.Warning("SendPcmFrame rejected: gateway null={IsNull}, stopped={Stopped}, started={Started}",
                    gw is null, gw?.Stopped, gw?.Started);
                return false;
            }

            var result = vc.SendPcmFrame(gw, data, 0, length);
            if (result <= 0)
                Log.Warning("SendPcmFrame returned {Result} ({Error})", result, (SendPcmError)result);
            return result > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't send pcm frame: {Message}", ex.Message);
            return false;
        }
    }

    public bool SendOpusFrame(VoiceClient vc, byte[] data, int length)
    {
        try
        {
            var gw = Volatile.Read(ref gateway);
            if (gw is null || gw.Stopped || !gw.Started)
            {
                Log.Warning("SendOpusFrame rejected: gateway null={IsNull}, stopped={Stopped}, started={Started}",
                    gw is null, gw?.Stopped, gw?.Started);
                return false;
            }

            var result = vc.SendOpusFrame(gw, data, 0, length);
            return result > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't send opus frame: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> RunGatewayAction(Func<VoiceGateway, Task> action)
    {
        var errorCount = 0;
        do
        {
            if (State == VoiceProxyState.Stopped)
                break;

            try
            {
                var gw = Volatile.Read(ref gateway);
                if (gw is null || !gw.ConnectingFinished.Task.IsCompleted)
                {
                    ++errorCount;
                    await Task.Delay(DELAY_ON_ERROR_MILISECONDS);
                    Log.Debug("Gateway is not ready");
                    continue;
                }

                await action(gw);
                errorCount = 0;
            }
            catch (Exception ex)
            {
                ++errorCount;
                await Task.Delay(DELAY_ON_ERROR_MILISECONDS);
                Log.Debug(ex, "Error performing proxy gateway action");
            }
        } while (errorCount is > 0 and <= MAX_ERROR_COUNT);

        return State != VoiceProxyState.Stopped && errorCount <= MAX_ERROR_COUNT;
    }

    public void SetGateway(VoiceGateway newGateway)
    {
        // Full fences on both sides: either this sees a new speaking state, or SetSpeakingInternal sees this gateway.
        var old = Interlocked.Exchange(ref gateway, newGateway);
        if (newGateway is not null)
            newGateway.Speaking = (VoiceSpeaking.State)Volatile.Read(ref _speaking);

        old?.Dispose();
    }

    public bool IsGatewayFor(ulong channelId, string token, string endpoint)
        => Volatile.Read(ref gateway) is { } gw && gw.IsFor(channelId, token, endpoint);

    public Task StartSpeakingAsync()
        => SetSpeakingInternalAsync(VoiceSpeaking.State.Microphone);

    public Task StopSpeakingAsync()
        => SetSpeakingInternalAsync(VoiceSpeaking.State.None);

    private Task SetSpeakingInternalAsync(VoiceSpeaking.State state)
    {
        Interlocked.Exchange(ref _speaking, (int)state);
        if (Volatile.Read(ref gateway) is { } gw)
            gw.Speaking = state;

        return RunGatewayAction(_sendSpeaking);
    }

    public Task StartGateway()
        => Volatile.Read(ref gateway)?.Start() ?? Task.CompletedTask;

    public Task StopGateway()
    {
        if (Volatile.Read(ref gateway) is { } gw)
            return gw.StopAsync();

        return Task.CompletedTask;
    }
}
