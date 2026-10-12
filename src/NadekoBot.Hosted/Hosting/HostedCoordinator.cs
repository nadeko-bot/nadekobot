using DryIoc;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.Hosted;

public sealed class HostedCoordinator(
    IHostApplicationLifetime lifetime,
    DiscordSocketClient client,
    TimeProvider time) : ICoordinator
{
    // Lets the command send its "restarting" reply before the process stops.
    public static readonly TimeSpan StopDelay = TimeSpan.FromSeconds(2);

    private const int FULL_UPTIME_PERCENT = 100;

    public static void Register(IContainer services, IHostApplicationLifetime lifetime, TimeProvider time)
        => services.RegisterDelegate<ICoordinator>(
            r => new HostedCoordinator(lifetime, r.Resolve<DiscordSocketClient>(), time),
            Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

    public bool RestartBot()
    {
        _ = Task.Run(StopInternalAsync);
        return true;
    }

    public void Die(bool graceful)
        => _ = Task.Run(StopInternalAsync);

    public bool RestartShard(int shardId)
        => RestartBot();

    public IList<ShardStatus> GetAllShardStatuses()
        =>
        [
            new ShardStatus
            {
                ConnectionState = client.ConnectionState,
                GuildCount = client.Guilds.Count,
                LastUpdate = time.GetUtcNow().UtcDateTime,
                ShardId = client.ShardId,
                // Only fills the bot's ShardStatus contract; the API does not expose it.
                UptimePercent = FULL_UPTIME_PERCENT
            }
        ];

    public int GetGuildCount()
        => client.Guilds.Count;

    public Task Reload()
        => Task.CompletedTask;

    private async Task StopInternalAsync()
    {
        await Task.Delay(StopDelay, time);
        ProcessRestarter.StopForSupervisor(lifetime);
    }
}
