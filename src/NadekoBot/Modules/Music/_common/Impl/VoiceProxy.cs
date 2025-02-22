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
        => gateway switch
        {
            { Started: true, Stopped: false } => VoiceProxyState.Started,
            { Stopped: false } => VoiceProxyState.Created,
            _ => VoiceProxyState.Stopped
        };


    private VoiceGateway gateway;

    public VoiceProxy(VoiceGateway initial)
        => gateway = initial;

    public bool SendPcmFrame(VoiceClient vc, Span<byte> data, int length)
    {
        try
        {
            var gw = gateway;
            if (gw is null || gw.Stopped || !gw.Started)
                return false;

            vc.SendPcmFrame(gw, data, 0, length);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't send pcm frame: {Message}", ex.Message);
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
                var gw = gateway;
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
        => gateway = newGateway;

    public Task StartSpeakingAsync()
        => RunGatewayAction(gw => gw.SendSpeakingAsync(VoiceSpeaking.State.Microphone));

    public Task StopSpeakingAsync()
        => RunGatewayAction(gw => gw.SendSpeakingAsync(VoiceSpeaking.State.None));

    public async Task StartGateway()
        => await gateway.Start();

    public Task StopGateway()
    {
        if (gateway is { } gw)
            return gw.StopAsync();

        return Task.CompletedTask;
    }
}