#nullable disable
namespace NadekoBot.Services;

public interface ICoordinator
{
    ValueTask<bool> RestartBotAsync();
    ValueTask DieAsync(bool graceful);
    ValueTask<bool> RestartShardAsync(int shardId);
    ValueTask<IList<ShardStatus>> GetAllShardStatusesAsync();
    ValueTask<int> GetGuildCountAsync();
    Task Reload();
}

public class ShardStatus
{
    public ConnectionState ConnectionState { get; set; }
    public DateTime LastUpdate { get; set; }
    public int ShardId { get; set; }
    public int GuildCount { get; set; }
    public double UptimePercent { get; set; }
}
