#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Permissions.Common;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions.Services;

public class PermissionService : IExecPreCommand, INService
{
    public int Priority { get; } = 0;

    //guildid, root permission
    public ConcurrentDictionary<ulong, PermissionCache> Cache { get; } = new();

    private readonly DbService _db;
    private readonly CommandHandler _cmd;
    private readonly IBotStrings _strings;
    private readonly IMessageSenderService _sender;

    public PermissionService(
        DiscordSocketClient client,
        DbService db,
        CommandHandler cmd,
        IBotStrings strings,
        IMessageSenderService sender)
    {
        _db = db;
        _cmd = cmd;
        _strings = strings;
        _sender = sender;

        using var uow = _db.GetDbContext();
        foreach (var x in uow.Set<GuildConfig>().PermissionsForAll(client.Guilds.ToArray().Select(x => x.Id).ToList()))
        {
            Cache.TryAdd(x.GuildId,
                new()
                {
                    Verbose = x.VerbosePermissions,
                    PermRole = x.PermissionRole,
                    Permissions = new(x.Permissions)
                });
        }
    }

    public PermissionCache GetCacheFor(ulong guildId)
    {
        if (!Cache.TryGetValue(guildId, out var pc))
        {
            using (var uow = _db.GetDbContext())
            {
                var config = uow.GuildConfigsForId(guildId, set => set.Include(x => x.Permissions));
                UpdateCache(config);
            }

            Cache.TryGetValue(guildId, out pc);
            if (pc is null)
                throw new("Cache is null.");
        }

        return pc;
    }

    public async Task AddPermissions(ulong guildId, params Permissionv2[] perms)
    {
        await using var uow = _db.GetDbContext();
        var config = uow.GcWithPermissionsFor(guildId);
        //var orderedPerms = new PermissionsCollection<Permissionv2>(config.Permissions);
        var max = config.Permissions.Max(x => x.Index); //have to set its index to be the highest
        foreach (var perm in perms)
        {
            perm.Index = ++max;
            config.Permissions.Add(perm);
        }

        await uow.SaveChangesAsync();
        UpdateCache(config);
    }

    public void UpdateCache(GuildConfig config)
        => Cache.AddOrUpdate(config.GuildId,
            new PermissionCache
            {
                Permissions = new(config.Permissions),
                PermRole = config.PermissionRole,
                Verbose = config.VerbosePermissions
            },
            (_, old) =>
            {
                old.Permissions = new(config.Permissions);
                old.PermRole = config.PermissionRole;
                old.Verbose = config.VerbosePermissions;
                return old;
            });

    public async Task<bool> ExecPreCommandAsync(ICommandContext ctx, string moduleName, CommandInfo command)
    {
        var guild = ctx.Guild;
        var msg = ctx.Message;
        var user = ctx.User;
        var channel = ctx.Channel;
        var commandName = command.Name.ToLowerInvariant();

        if (guild is null)
            return false;

        var resetCommand = commandName == "resetperms";

        var pc = GetCacheFor(guild.Id);
        if (!resetCommand
            && !pc.Permissions.CheckPermissions(msg.Author, msg.Channel, commandName, moduleName, out var index))
        {
            if (pc.Verbose)
            {
                try
                {
                    await _sender.Response(channel)
                                 .Error(_strings.GetText(strs.perm_prevent(index + 1,
                                         Format.Bold(pc.Permissions[index]
                                                       .GetCommand(_cmd.GetPrefix(guild), (SocketGuild)guild))),
                                     guild.Id))
                                 .SendAsync();
                }
                catch
                {
                }
            }

            return true;
        }


        if (moduleName == nameof(Permissions))
        {
            if (user is not IGuildUser guildUser)
                return true;

            if (guildUser.GuildPermissions.Administrator)
                return false;

            var permRole = pc.PermRole;
            if (!ulong.TryParse(permRole, out var rid))
                rid = 0;
            string returnMsg;
            IRole role;
            if (string.IsNullOrWhiteSpace(permRole) || (role = guild.GetRole(rid)) is null)
            {
                returnMsg = "You need Admin permissions in order to use permission commands.";
                if (pc.Verbose)
                {
                    try { await _sender.Response(channel).Error(returnMsg).SendAsync(); }
                    catch { }
                }

                return true;
            }

            if (!guildUser.RoleIds.Contains(rid))
            {
                returnMsg = $"You need the {Format.Bold(role.Name)} role in order to use permission commands.";
                if (pc.Verbose)
                {
                    try { await _sender.Response(channel).Error(returnMsg).SendAsync(); }
                    catch { }
                }

                return true;
            }

            return false;
        }

        return false;
    }

    public async Task Reset(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var config = uow.GcWithPermissionsFor(guildId);
        config.Permissions = Permissionv2.GetDefaultPermlist;
        await uow.SaveChangesAsync();
        UpdateCache(config);
    }
}