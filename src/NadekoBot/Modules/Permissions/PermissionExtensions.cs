#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions.Common;

public static class PermissionExtensions
{
    public static bool CheckPermissions(
        this IEnumerable<Permissionv2> permsEnumerable,
        IUser user,
        IMessageChannel message,
        string commandName,
        string moduleName,
        out int permIndex)
    {
        var perms = permsEnumerable as List<Permissionv2> ?? permsEnumerable.ToList();

        for (var i = perms.Count - 1; i >= 0; i--)
        {
            var perm = perms[i];

            var result = perm.CheckPermission(user, message, commandName, moduleName);

            if (result is null)
                continue;
            permIndex = i;
            return result.Value;
        }

        permIndex = -1; //defaut behaviour
        return true;
    }

    //null = not applicable
    //true = applicable, allowed
    //false = applicable, not allowed
    public static bool? CheckPermission(
        this Permissionv2 perm,
        IUser user,
        IMessageChannel channel,
        string commandName,
        string moduleName)
    {
        if (!((perm.SecondaryTarget == SecondaryPermissionType.Command
               && string.Equals(perm.SecondaryTargetName, commandName, StringComparison.InvariantCultureIgnoreCase))
              || (perm.SecondaryTarget == SecondaryPermissionType.Module
                  && string.Equals(perm.SecondaryTargetName, moduleName, StringComparison.InvariantCultureIgnoreCase))
              || perm.SecondaryTarget == SecondaryPermissionType.AllModules))
            return null;

        var guildUser = user as IGuildUser;

        switch (perm.PrimaryTarget)
        {
            case PrimaryPermissionType.User:
                if (perm.PrimaryTargetId == user.Id)
                    return perm.State;
                break;
            case PrimaryPermissionType.Channel:
                if (perm.PrimaryTargetId == channel.Id)
                    return perm.State;
                break;
            case PrimaryPermissionType.Role:
                if (guildUser is null)
                    break;
                if (guildUser.RoleIds.Contains(perm.PrimaryTargetId))
                    return perm.State;
                break;
            case PrimaryPermissionType.Server:
                if (guildUser is null)
                    break;
                return perm.State;
        }

        return null;
    }

    public static string GetCommand(this Permissionv2 perm, string prefix, SocketGuild guild = null)
    {
        var com = string.Empty;
        switch (perm.PrimaryTarget)
        {
            case PrimaryPermissionType.User:
                com += "u";
                break;
            case PrimaryPermissionType.Channel:
                com += "c";
                break;
            case PrimaryPermissionType.Role:
                com += "r";
                break;
            case PrimaryPermissionType.Server:
                com += "s";
                break;
        }

        switch (perm.SecondaryTarget)
        {
            case SecondaryPermissionType.Module:
                com += "m";
                break;
            case SecondaryPermissionType.Command:
                com += "c";
                break;
            case SecondaryPermissionType.AllModules:
                com = "a" + com + "m";
                break;
        }

        var secName = perm.SecondaryTarget == SecondaryPermissionType.Command && !perm.IsCustomCommand
            ? prefix + perm.SecondaryTargetName
            : perm.SecondaryTargetName;
        com += " " + (perm.SecondaryTargetName != "*" ? secName + " " : "") + (perm.State ? "enable" : "disable") + " ";

        switch (perm.PrimaryTarget)
        {
            case PrimaryPermissionType.User:
                com += guild?.GetUser(perm.PrimaryTargetId)?.ToString() ?? $"<@{perm.PrimaryTargetId}>";
                break;
            case PrimaryPermissionType.Channel:
                com += $"<#{perm.PrimaryTargetId}>";
                break;
            case PrimaryPermissionType.Role:
                com += guild?.GetRole(perm.PrimaryTargetId)?.ToString() ?? $"<@&{perm.PrimaryTargetId}>";
                break;
            case PrimaryPermissionType.Server:
                break;
        }

        return prefix + com;
    }
}