#nullable disable
using NadekoBot.Voice;
using System.Reflection;

namespace NadekoBot.Modules.Music.Services;

public sealed class AyuVoiceStateService : INService
{
    // public delegate Task VoiceProxyUpdatedDelegate(ulong guildId, IVoiceProxy proxy);
    // public event VoiceProxyUpdatedDelegate OnVoiceProxyUpdate = delegate { return Task.CompletedTask; };

    private readonly ConcurrentDictionary<ulong, IVoiceProxy> _voiceProxies = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _voiceGatewayLocks = new();

    private readonly DiscordSocketClient _client;
    private readonly MethodInfo _sendVoiceStateUpdateMethodInfo;
    private readonly object _dnetApiClient;
    private readonly ulong _currentUserId;

    public AyuVoiceStateService(DiscordSocketClient client)
    {
        _client = client;
        _currentUserId = _client.CurrentUser.Id;

        var prop = _client.GetType()
                          .GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)
                          .First(x => x.Name == "ApiClient" && x.PropertyType.Name == "DiscordSocketApiClient");
        _dnetApiClient = prop.GetValue(_client, null);
        _sendVoiceStateUpdateMethodInfo = _dnetApiClient.GetType()
                                                        .GetMethod("SendVoiceStateUpdateAsync",
                                                        [
                                                            typeof(ulong), typeof(ulong?), typeof(bool),
                                                                typeof(bool), typeof(RequestOptions)
                                                        ]);

        _client.LeftGuild += ClientOnLeftGuild;
    }

    private Task ClientOnLeftGuild(SocketGuild guild)
    {
        if (_voiceProxies.TryRemove(guild.Id, out var proxy))
        {
            proxy.StopGateway();
            proxy.SetGateway(null);
        }

        return Task.CompletedTask;
    }

    private Task InvokeSendVoiceStateUpdateAsync(
            ulong guildId,
            ulong? channelId = null,
            bool isDeafened = false,
            bool isMuted = false)
        // return _voiceStateUpdate(guildId, channelId, isDeafened, isMuted);
        => (Task)_sendVoiceStateUpdateMethodInfo.Invoke(_dnetApiClient,
            [guildId, channelId, isMuted, isDeafened, null]);

    private Task SendLeaveVoiceChannelInternalAsync(ulong guildId)
        => InvokeSendVoiceStateUpdateAsync(guildId);

    private Task SendJoinVoiceChannelInternalAsync(ulong guildId, ulong channelId)
        => InvokeSendVoiceStateUpdateAsync(guildId, channelId);

    private SemaphoreSlim GetVoiceGatewayLock(ulong guildId)
        => _voiceGatewayLocks.GetOrAdd(guildId, new SemaphoreSlim(1, 1));

    private async Task LeaveVoiceChannelInternalAsync(ulong guildId)
    {
        var complete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            if (user is SocketGuildUser guildUser && guildUser.Guild.Id == guildId && newState.VoiceChannel?.Id is null)
                complete.TrySetResult(true);

            return Task.CompletedTask;
        }

        try
        {
            _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;

            if (_voiceProxies.TryGetValue(guildId, out var proxy))
            {
                _ = proxy.StopGateway();
                proxy.SetGateway(null);
            }

            await SendLeaveVoiceChannelInternalAsync(guildId);
            await Task.WhenAny(Task.Delay(1500), complete.Task);
        }
        finally
        {
            _client.UserVoiceStateUpdated -= OnUserVoiceStateUpdated;
        }
    }

    public async Task LeaveVoiceChannel(ulong guildId)
    {
        var gwLock = GetVoiceGatewayLock(guildId);
        await gwLock.WaitAsync();
        try
        {
            await LeaveVoiceChannelInternalAsync(guildId);
        }
        finally
        {
            gwLock.Release();
        }
    }

    private async Task<IVoiceProxy> InternalConnectToVcAsync(ulong guildId, ulong channelId)
    {
        var voiceStateUpdatedSource =
            new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var voiceServerUpdatedSource =
            new TaskCompletionSource<SocketVoiceServer>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            if (user is SocketGuildUser guildUser && guildUser.Guild.Id == guildId)
            {
                if (newState.VoiceChannel?.Id == channelId)
                    voiceStateUpdatedSource.TrySetResult(newState.VoiceSessionId);

                voiceStateUpdatedSource.TrySetResult(null);
            }

            return Task.CompletedTask;
        }

        Task OnVoiceServerUpdated(SocketVoiceServer data)
        {
            if (data.Guild.Id == guildId)
                voiceServerUpdatedSource.TrySetResult(data);

            return Task.CompletedTask;
        }

        try
        {
            _client.VoiceServerUpdated += OnVoiceServerUpdated;
            _client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;

            await SendJoinVoiceChannelInternalAsync(guildId, channelId);

            // create a delay task, how much to wait for gateway response
            using var cts = new CancellationTokenSource();
            var delayTask = Task.Delay(2500, cts.Token);

            // either delay or successful voiceStateUpdate
            var maybeUpdateTask = Task.WhenAny(delayTask, voiceStateUpdatedSource.Task);
            // either delay or successful voiceServerUpdate
            var maybeServerTask = Task.WhenAny(delayTask, voiceServerUpdatedSource.Task);

            // wait for both to end (max 1s) and check if either of them is a delay task
            var results = await Task.WhenAll(maybeUpdateTask, maybeServerTask);
            if (results[0] == delayTask || results[1] == delayTask)
                // if either is delay, return null - connection unsuccessful
                return null;
            else
                cts.Cancel();

            // if both are succesful, that means we can safely get
            // the values from  completion sources

            var session = await voiceStateUpdatedSource.Task;

            // session can be null. Means we disconnected, or connected to the wrong channel (?!)
            if (session is null)
                return null;

            var voiceServerData = await voiceServerUpdatedSource.Task;

            VoiceGateway CreateVoiceGatewayLocal()
            {
                return new(guildId, _currentUserId, session, voiceServerData.Token, voiceServerData.Endpoint);
            }

            var current = _voiceProxies.AddOrUpdate(guildId,
                _ => new VoiceProxy(CreateVoiceGatewayLocal()),
                (gid, currentProxy) =>
                {
                    _ = currentProxy.StopGateway();
                    currentProxy.SetGateway(CreateVoiceGatewayLocal());
                    return currentProxy;
                });

            _ = current.StartGateway(); // don't await, this blocks until gateway is closed
            return current;
        }
        finally
        {
            _client.VoiceServerUpdated -= OnVoiceServerUpdated;
            _client.UserVoiceStateUpdated -= OnUserVoiceStateUpdated;
        }
    }

    public async Task<IVoiceProxy> JoinVoiceChannel(ulong guildId, ulong channelId, bool forceReconnect = true)
    {
        var gwLock = GetVoiceGatewayLock(guildId);
        await gwLock.WaitAsync();
        try
        {
            await LeaveVoiceChannelInternalAsync(guildId);
            return await InternalConnectToVcAsync(guildId, channelId);
        }
        finally
        {
            gwLock.Release();
        }
    }

    public bool TryGetProxy(ulong guildId, out IVoiceProxy proxy)
        => _voiceProxies.TryGetValue(guildId, out proxy);
}