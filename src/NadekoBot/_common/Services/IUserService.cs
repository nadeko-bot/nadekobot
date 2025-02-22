using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp.Services;

public interface IUserService
{
    Task<DiscordUser?> GetUserAsync(ulong userId);
}