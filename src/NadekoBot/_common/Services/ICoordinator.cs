#nullable disable
namespace NadekoBot.Services;

public interface ICoordinator
{
    bool RestartBot();
    void Die(bool graceful);
    bool RestartShard(int shardId);
    IList<ShardStatus> GetAllShardStatuses();
    int GetGuildCount();
    Task Reload();
}

public class ShardStatus
{
    public ConnectionState ConnectionState { get; set; }
    public DateTime LastUpdate { get; set; }
    public int ShardId { get; set; }
    public int GuildCount { get; set; }
}