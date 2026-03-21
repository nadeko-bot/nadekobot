namespace NadekoBot.Modules.Owner.Dangerous;

/// <summary>
/// Report from a shard containing the guild IDs it is connected to.
/// </summary>
public sealed class KeepReport
{
    /// <summary>
    /// The shard ID that sent this report.
    /// </summary>
    public required int ShardId { get; init; }
    
    /// <summary>
    /// Array of guild IDs the shard is connected to.
    /// </summary>
    public required ulong[] GuildIds { get; init; }
}
