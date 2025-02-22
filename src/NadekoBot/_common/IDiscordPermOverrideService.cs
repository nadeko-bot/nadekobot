#nullable disable
namespace Nadeko.Common;

public interface IDiscordPermOverrideService
{
    bool TryGetOverrides(ulong guildId, string commandName, out NadekoBot.Db.GuildPerm? perm);
}