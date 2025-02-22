using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Modules.Administration.DangerousCommands;

public sealed class CleanupId
{
    [Key]
    public ulong GuildId { get; set; }
}