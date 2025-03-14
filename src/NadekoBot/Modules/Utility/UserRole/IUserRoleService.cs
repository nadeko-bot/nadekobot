using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Modules.Utility.UserRole;

public interface IUserRoleService
{
    /// <summary>
    /// Assigns a role to a user and updates both database and Discord
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="roleId">ID of the role</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> AddRoleAsync(ulong guildId, ulong userId, ulong roleId);

    /// <summary>
    /// Removes a role from a user and updates both database and Discord
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="roleId">ID of the role</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> RemoveRoleAsync(ulong guildId, ulong userId, ulong roleId);
    
    /// <summary>
    /// Gets all user roles for a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    Task<IReadOnlyCollection<UserRole>> ListRolesAsync(ulong guildId);
    
    /// <summary>
    /// Gets all roles for a specific user in a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    Task<IReadOnlyCollection<UserRole>> ListUserRolesAsync(ulong guildId, ulong userId);

    /// <summary>
    /// Sets the custom color for a user's role and updates both database and Discord
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="roleId">ID of the role</param>
    /// <param name="color">Hex color code</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SetRoleColorAsync(ulong guildId, ulong userId, ulong roleId, Rgba32 color);

    /// <summary>
    /// Sets the custom name for a user's role and updates both database and Discord
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="roleId">ID of the role</param>
    /// <param name="name">New role name</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SetRoleNameAsync(ulong guildId, ulong userId, ulong roleId, string name);

    /// <summary>
    /// Sets the custom icon for a user's role and updates both database and Discord
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="roleId">ID of the role</param>
    /// <param name="icon">Icon URL or emoji</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> SetRoleIconAsync(ulong guildId, ulong userId, ulong roleId, string icon);
    
    /// <summary>
    /// Checks if a user has a specific role assigned
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="roleId">ID of the role</param>
    /// <returns>True if the user has the role, false otherwise</returns>
    Task<bool> UserOwnsRoleAsync(ulong guildId, ulong userId, ulong roleId);
}