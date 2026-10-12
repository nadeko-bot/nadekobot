namespace NadekoBot.Common;

public static class RoleChecks
{
    // A role the bot cannot assign would make a later reward or punishment fail with a 403 nobody sees.
    public static bool CanBotAssign(SocketGuild guild, SocketRole role)
    {
        var bot = guild.CurrentUser;
        return bot.GuildPermissions.ManageRoles
               && !role.IsEveryone
               && !role.IsManaged
               && role.Position < bot.Hierarchy;
    }
}
