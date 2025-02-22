namespace NadekoBot.Modules.Administration.DangerousCommands;

public sealed class KeepReport
{
    public required int ShardId { get; init; }
    public required ulong[] GuildIds { get; init; }
}