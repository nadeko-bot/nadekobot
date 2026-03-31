using NadekoBot.Db.Models;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Administration.Services;

public interface IReactionRoleService
{
    Task<OneOf<Success, Error>> AddReactionRole(
        IGuild guild,
        IMessage msg,
        string emote,
        IRole role,
        int group = 0,
        int levelReq = 0);

    Task<IReadOnlyCollection<ReactionRoleV2>> GetReactionRolesAsync(ulong guildId);

    Task<bool> RemoveReactionRoles(ulong guildId, ulong messageId);

    Task<int> RemoveAllReactionRoles(ulong guildId);

    Task<IReadOnlyCollection<IEmote>> TransferReactionRolesAsync(ulong guildId, ulong fromMessageId, ulong toMessageId);

    Task<IReadOnlyCollection<ReactionRoleV2>> GetReactionRolesForRoleAsync(ulong guildId, ulong messageId, ulong roleId);

    Task<string?> RemoveReactionRoleAsync(ulong guildId, ulong messageId, string emote);

    Task<string?> ChangeReactionRoleEmoteAsync(ulong guildId, ulong messageId, string oldEmote, string newEmote);
}