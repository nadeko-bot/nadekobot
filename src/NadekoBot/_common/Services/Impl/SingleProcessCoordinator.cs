#nullable disable
using System.Diagnostics;

namespace NadekoBot.Services;

public class SingleProcessCoordinator : ICoordinator
{
    private readonly IBotCreds _creds;
    private readonly DiscordSocketClient _client;

    public SingleProcessCoordinator(IBotCreds creds, DiscordSocketClient client)
    {
        _creds = creds;
        _client = client;
    }

    public bool RestartBot()
    {
        if (string.IsNullOrWhiteSpace(_creds.RestartCommand?.Cmd)
            || string.IsNullOrWhiteSpace(_creds.RestartCommand?.Args))
        {
            Log.Error("You must set RestartCommand.Cmd and RestartCommand.Args in creds.yml");
            return false;
        }

        Process.Start(_creds.RestartCommand.Cmd, _creds.RestartCommand.Args);
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Die();
        });
        return true;
    }

    public void Die(bool graceful = false)
        => Environment.Exit(0);

    public bool RestartShard(int shardId)
        => RestartBot();

    public IList<ShardStatus> GetAllShardStatuses()
        => new[]
        {
            new ShardStatus
            {
                ConnectionState = _client.ConnectionState,
                GuildCount = _client.Guilds.Count,
                LastUpdate = DateTime.UtcNow,
                ShardId = _client.ShardId
            }
        };

    public int GetGuildCount()
        => _client.Guilds.Count;

    public Task Reload()
        => Task.CompletedTask;
}