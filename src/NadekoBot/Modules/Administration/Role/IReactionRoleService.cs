#nullable disable
using NadekoBot.Db.Models;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Administration.Services;

public interface IReactionRoleService
{
    /// <summary>
    /// Adds a single reaction role
    /// </summary>
    /// <param name="guild">Guild where to add a reaction role</param>
    /// <param name="msg">Message to which to add a reaction role</param>
    /// <param name="emote"></param>
    /// <param name="role"></param>
    /// <param name="group"></param>
    /// <param name="levelReq"></param>
    /// <returns>The result of the operation</returns>
    Task<OneOf<Success, Error>> AddReactionRole(
        IGuild guild,
        IMessage msg,
        string emote,
        IRole role,
        int group = 0,
        int levelReq = 0);

    /// <summary>
    /// Get all reaction roles on the specified server
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns></returns>
    Task<IReadOnlyCollection<ReactionRoleV2>> GetReactionRolesAsync(ulong guildId);

    /// <summary>
    /// Remove reaction roles on the specified message
    /// </summary>
    /// <param name="guildId"></param>
    /// <param name="messageId"></param>
    /// <returns></returns>
    Task<bool> RemoveReactionRoles(ulong guildId, ulong messageId);

    /// <summary>
    /// Remove all reaction roles in the specified server
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns></returns>
    Task<int> RemoveAllReactionRoles(ulong guildId);

    Task<IReadOnlyCollection<IEmote>> TransferReactionRolesAsync(ulong guildId, ulong fromMessageId, ulong toMessageId);
}