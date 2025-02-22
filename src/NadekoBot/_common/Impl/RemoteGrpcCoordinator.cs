#nullable disable
using Grpc.Core;
using Grpc.Net.Client;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Coordinator;

namespace NadekoBot.Services;

public class RemoteGrpcCoordinator : ICoordinator, IReadyExecutor
{
    private readonly Coordinator.Coordinator.CoordinatorClient _coordClient;
    private readonly DiscordSocketClient _client;

    public RemoteGrpcCoordinator(IBotCreds creds, DiscordSocketClient client)
    {
        var coordUrl = string.IsNullOrWhiteSpace(creds.CoordinatorUrl) ? "http://localhost:3442" : creds.CoordinatorUrl;

        var channel = GrpcChannel.ForAddress(coordUrl);
        _coordClient = new(channel);
        _client = client;
    }

    public bool RestartBot()
    {
        _coordClient.RestartAllShards(new());

        return true;
    }

    public void Die(bool graceful)
        => _coordClient.Die(new()
        {
            Graceful = graceful
        });

    public bool RestartShard(int shardId)
    {
        _coordClient.RestartShard(new()
        {
            ShardId = shardId
        });

        return true;
    }

    public IList<ShardStatus> GetAllShardStatuses()
    {
        var res = _coordClient.GetAllStatuses(new());

        return res.Statuses.ToArray()
                  .Map(s => new ShardStatus
                  {
                      ConnectionState = FromCoordConnState(s.State),
                      GuildCount = s.GuildCount,
                      ShardId = s.ShardId,
                      LastUpdate = s.LastUpdate.ToDateTime()
                  });
    }

    public int GetGuildCount()
    {
        var res = _coordClient.GetAllStatuses(new());

        return res.Statuses.Sum(x => x.GuildCount);
    }

    public async Task Reload()
        => await _coordClient.ReloadAsync(new());

    public Task OnReadyAsync()
    {
        Task.Run(async () =>
        {
            var gracefulImminent = false;
            while (true)
            {
                try
                {
                    var reply = await _coordClient.HeartbeatAsync(new()
                        {
                            State = ToCoordConnState(_client.ConnectionState),
                            GuildCount =
                                _client.ConnectionState == ConnectionState.Connected ? _client.Guilds.Count : 0,
                            ShardId = _client.ShardId
                        },
                        deadline: DateTime.UtcNow + TimeSpan.FromSeconds(10));
                    gracefulImminent = reply.GracefulImminent;
                }
                catch (RpcException ex)
                {
                    if (!gracefulImminent)
                    {
                        Log.Warning(ex, "Hearbeat failed and graceful shutdown was not expected: {Message}",
                            ex.Message);
                        break;
                    }

                    Log.Information("Coordinator is restarting gracefully. Waiting...");
                    await Task.Delay(30_000);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected heartbeat exception: {Message}", ex.Message);
                    break;
                }

                await Task.Delay(7500);
            }

            Environment.Exit(0);
        });

        return Task.CompletedTask;
    }

    private ConnState ToCoordConnState(ConnectionState state)
        => state switch
        {
            ConnectionState.Connecting => ConnState.Connecting,
            ConnectionState.Connected => ConnState.Connected,
            _ => ConnState.Disconnected
        };

    private ConnectionState FromCoordConnState(ConnState state)
        => state switch
        {
            ConnState.Connecting => ConnectionState.Connecting,
            ConnState.Connected => ConnectionState.Connected,
            _ => ConnectionState.Disconnected
        };
}