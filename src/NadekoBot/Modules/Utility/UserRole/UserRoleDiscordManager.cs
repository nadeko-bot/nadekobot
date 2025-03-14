namespace NadekoBot.Modules.Utility.UserRole;

public class UserRoleDiscordManager(DiscordSocketClient client) : IDiscordRoleManager, INService
{
    /// <summary>
    /// Modifies a role's properties in Discord
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="roleId">ID of the role to modify</param>
    /// <param name="name">New name for the role (optional)</param>
    /// <param name="color">New color for the role (optional)</param>
    /// <param name="image">New emoji for the role (optional)</param>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> ModifyRoleAsync(
        ulong guildId,
        ulong roleId,
        string? name = null,
        Color? color = null,
        Image? image = null
    )
    {
        try
        {
            var guild = client.GetGuild(guildId);
            if (guild is null)
                return false;

            var role = guild.GetRole(roleId);
            if (role is null)
                return false;

            await role.ModifyAsync(properties =>
            {
                if (name is not null)
                    properties.Name = name;

                if (color is not null)
                    properties.Color = color.Value;

                if (image is not null)
                    properties.Icon = image;
            });

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to modify role {RoleId}", roleId);
            return false;
        }
    }
}