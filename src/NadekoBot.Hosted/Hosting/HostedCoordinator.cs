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

    public ValueTask<bool> RestartBotAsync()
    {
        _ = Task.Run(StopInternalAsync);
        return ValueTask.FromResult(true);
    }

    public ValueTask DieAsync(bool graceful)
    {
        _ = Task.Run(StopInternalAsync);
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> RestartShardAsync(int shardId)
        => RestartBotAsync();

    public ValueTask<IList<ShardStatus>> GetAllShardStatusesAsync()
        => ValueTask.FromResult<IList<ShardStatus>>(
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
        ]);

    public ValueTask<int> GetGuildCountAsync()
        => ValueTask.FromResult(client.Guilds.Count);

    public Task Reload()
        => Task.CompletedTask;

    private async Task StopInternalAsync()
    {
        await Task.Delay(StopDelay, time);
        ProcessRestarter.StopForSupervisor(lifetime);
    }
}
