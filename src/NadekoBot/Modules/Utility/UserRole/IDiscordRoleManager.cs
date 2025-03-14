namespace NadekoBot.Modules.Utility.UserRole;

public interface IDiscordRoleManager
{
    /// <summary>
    /// Modifies a role's properties in Discord
    /// </summary>
    /// <param name="guildId">The ID of the guild containing the role</param>
    /// <param name="roleId">ID of the role to modify</param>
    /// <param name="name">New name for the role (optional)</param>
    /// <param name="color">New color for the role (optional)</param>
    /// <param name="image">Image for the role (optional)</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> ModifyRoleAsync(
        ulong guildId,
        ulong roleId,
        string? name = null,
        Color? color = null,
        Image? image = null
    );
}