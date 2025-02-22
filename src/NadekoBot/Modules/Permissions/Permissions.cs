#nullable disable
using NadekoBot.Common.TypeReaders;
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Permissions.Common;
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions;

public partial class Permissions : NadekoModule<PermissionService>
{
    public enum Reset { Reset }

    private readonly DbService _db;

    public Permissions(DbService db)
        => _db = db;

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task Verbose(PermissionAction action = null)
    {
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.GcWithPermissionsFor(ctx.Guild.Id);
            if (action is null)
                action = new(!config.VerbosePermissions); // New behaviour, can toggle.
            config.VerbosePermissions = action.Value;
            await uow.SaveChangesAsync();
            _service.UpdateCache(config);
        }

        if (action.Value)
            await Response().Confirm(strs.verbose_true).SendAsync();
        else
            await Response().Confirm(strs.verbose_false).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(0)]
    public async Task PermRole([Leftover] IRole role = null)
    {
        if (role is not null && role == role.Guild.EveryoneRole)
            return;

        if (role is null)
        {
            var cache = _service.GetCacheFor(ctx.Guild.Id);
            if (!ulong.TryParse(cache.PermRole, out var roleId)
                || (role = ((SocketGuild)ctx.Guild).GetRole(roleId)) is null)
                await Response().Confirm(strs.permrole_not_set).SendAsync();
            else
                await Response().Confirm(strs.permrole(Format.Bold(role.ToString()))).SendAsync();
            return;
        }

        await using (var uow = _db.GetDbContext())
        {
            var config = uow.GcWithPermissionsFor(ctx.Guild.Id);
            config.PermissionRole = role.Id.ToString();
            uow.SaveChanges();
            _service.UpdateCache(config);
        }

        await Response().Confirm(strs.permrole_changed(Format.Bold(role.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPerm.Administrator)]
    [Priority(1)]
    public async Task PermRole(Reset _)
    {
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.GcWithPermissionsFor(ctx.Guild.Id);
            config.PermissionRole = null;
            await uow.SaveChangesAsync();
            _service.UpdateCache(config);
        }

        await Response().Confirm(strs.permrole_reset).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task ListPerms(int page = 1)
    {
        if (page < 1)
            return;

        IList<Permissionv2> perms;

        if (_service.Cache.TryGetValue(ctx.Guild.Id, out var permCache))
            perms = permCache.Permissions.Source.ToList();
        else
            perms = Permissionv2.GetDefaultPermlist;

        var startPos = 20 * (page - 1);
        var toSend = Format.Bold(GetText(strs.page(page)))
                     + "\n\n"
                     + string.Join("\n",
                         perms.Reverse()
                              .Skip(startPos)
                              .Take(20)
                              .Select(p =>
                              {
                                  var str =
                                      $"`{p.Index + 1}.` {Format.Bold(p.GetCommand(prefix, (SocketGuild)ctx.Guild))}";
                                  if (p.Index == 0)
                                      str += $" [{GetText(strs.uneditable)}]";
                                  return str;
                              }));

        await Response().Confirm(toSend).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task RemovePerm(int index)
    {
        index -= 1;
        if (index < 0)
            return;
        try
        {
            Permissionv2 p;
            await using (var uow = _db.GetDbContext())
            {
                var config = uow.GcWithPermissionsFor(ctx.Guild.Id);
                var permsCol = new PermissionsCollection<Permissionv2>(config.Permissions);
                p = permsCol[index];
                permsCol.RemoveAt(index);
                uow.Remove(p);
                await uow.SaveChangesAsync();
                _service.UpdateCache(config);
            }

            await Response()
                  .Confirm(strs.removed(index + 1,
                      Format.Code(p.GetCommand(prefix, (SocketGuild)ctx.Guild))))
                  .SendAsync();
        }
        catch (IndexOutOfRangeException)
        {
            await Response().Error(strs.perm_out_of_range).SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task MovePerm(int from, int to)
    {
        from -= 1;
        to -= 1;
        if (!(from == to || from < 0 || to < 0))
        {
            try
            {
                Permissionv2 fromPerm;
                await using (var uow = _db.GetDbContext())
                {
                    var config = uow.GcWithPermissionsFor(ctx.Guild.Id);
                    var permsCol = new PermissionsCollection<Permissionv2>(config.Permissions);

                    var fromFound = from < permsCol.Count;
                    var toFound = to < permsCol.Count;

                    if (!fromFound)
                    {
                        await Response().Error(strs.perm_not_found(++from)).SendAsync();
                        return;
                    }

                    if (!toFound)
                    {
                        await Response().Error(strs.perm_not_found(++to)).SendAsync();
                        return;
                    }

                    fromPerm = permsCol[from];

                    permsCol.RemoveAt(from);
                    permsCol.Insert(to, fromPerm);
                    await uow.SaveChangesAsync();
                    _service.UpdateCache(config);
                }

                await Response()
                      .Confirm(strs.moved_permission(
                          Format.Code(fromPerm.GetCommand(prefix, (SocketGuild)ctx.Guild)),
                          ++from,
                          ++to))
                      .SendAsync();

                return;
            }
            catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
            }
        }

        await Response().Confirm(strs.perm_out_of_range).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task SrvrCmd(CommandOrExprInfo command, PermissionAction action)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Server,
                PrimaryTargetId = 0,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            });

        if (action.Value)
            await Response().Confirm(strs.sx_enable(Format.Code(command.Name), GetText(strs.of_command))).SendAsync();
        else
            await Response().Confirm(strs.sx_disable(Format.Code(command.Name), GetText(strs.of_command))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task SrvrMdl(ModuleOrExpr module, PermissionAction action)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Server,
                PrimaryTargetId = 0,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            });

        if (action.Value)
            await Response().Confirm(strs.sx_enable(Format.Code(module.Name), GetText(strs.of_module))).SendAsync();
        else
            await Response().Confirm(strs.sx_disable(Format.Code(module.Name), GetText(strs.of_module))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task UsrCmd(CommandOrExprInfo command, PermissionAction action, [Leftover] IGuildUser user)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = user.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            });

        if (action.Value)
        {
            await Response()
                  .Confirm(strs.ux_enable(Format.Code(command.Name),
                      GetText(strs.of_command),
                      Format.Code(user.ToString())))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.ux_disable(Format.Code(command.Name),
                      GetText(strs.of_command),
                      Format.Code(user.ToString())))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task UsrMdl(ModuleOrExpr module, PermissionAction action, [Leftover] IGuildUser user)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = user.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            });

        if (action.Value)
        {
            await Response()
                  .Confirm(strs.ux_enable(Format.Code(module.Name),
                      GetText(strs.of_module),
                      Format.Code(user.ToString())))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.ux_disable(Format.Code(module.Name),
                      GetText(strs.of_module),
                      Format.Code(user.ToString())))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task RoleCmd(CommandOrExprInfo command, PermissionAction action, [Leftover] IRole role)
    {
        if (role == role.Guild.EveryoneRole)
            return;

        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Role,
                PrimaryTargetId = role.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            });

        if (action.Value)
        {
            await Response()
                  .Confirm(strs.rx_enable(Format.Code(command.Name),
                      GetText(strs.of_command),
                      Format.Code(role.Name)))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.rx_disable(Format.Code(command.Name),
                      GetText(strs.of_command),
                      Format.Code(role.Name)))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task RoleMdl(ModuleOrExpr module, PermissionAction action, [Leftover] IRole role)
    {
        if (role == role.Guild.EveryoneRole)
            return;

        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Role,
                PrimaryTargetId = role.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            });


        if (action.Value)
        {
            await Response()
                  .Confirm(strs.rx_enable(Format.Code(module.Name),
                      GetText(strs.of_module),
                      Format.Code(role.Name)))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.rx_disable(Format.Code(module.Name),
                      GetText(strs.of_module),
                      Format.Code(role.Name)))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task ChnlCmd(CommandOrExprInfo command, PermissionAction action, [Leftover] ITextChannel chnl)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Channel,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.Command,
                SecondaryTargetName = command.Name.ToLowerInvariant(),
                State = action.Value,
                IsCustomCommand = command.IsCustom
            });

        if (action.Value)
        {
            await Response()
                  .Confirm(strs.cx_enable(Format.Code(command.Name),
                      GetText(strs.of_command),
                      Format.Code(chnl.Name)))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.cx_disable(Format.Code(command.Name),
                      GetText(strs.of_command),
                      Format.Code(chnl.Name)))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task ChnlMdl(ModuleOrExpr module, PermissionAction action, [Leftover] ITextChannel chnl)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Channel,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.Module,
                SecondaryTargetName = module.Name.ToLowerInvariant(),
                State = action.Value
            });

        if (action.Value)
        {
            await Response()
                  .Confirm(strs.cx_enable(Format.Code(module.Name),
                      GetText(strs.of_module),
                      Format.Code(chnl.Name)))
                  .SendAsync();
        }
        else
        {
            await Response()
                  .Confirm(strs.cx_disable(Format.Code(module.Name),
                      GetText(strs.of_module),
                      Format.Code(chnl.Name)))
                  .SendAsync();
        }
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task AllChnlMdls(PermissionAction action, [Leftover] ITextChannel chnl)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Channel,
                PrimaryTargetId = chnl.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            });

        if (action.Value)
            await Response().Confirm(strs.acm_enable(Format.Code(chnl.Name))).SendAsync();
        else
            await Response().Confirm(strs.acm_disable(Format.Code(chnl.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task AllRoleMdls(PermissionAction action, [Leftover] IRole role)
    {
        if (role == role.Guild.EveryoneRole)
            return;

        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.Role,
                PrimaryTargetId = role.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            });

        if (action.Value)
            await Response().Confirm(strs.arm_enable(Format.Code(role.Name))).SendAsync();
        else
            await Response().Confirm(strs.arm_disable(Format.Code(role.Name))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task AllUsrMdls(PermissionAction action, [Leftover] IUser user)
    {
        await _service.AddPermissions(ctx.Guild.Id,
            new Permissionv2
            {
                PrimaryTarget = PrimaryPermissionType.User,
                PrimaryTargetId = user.Id,
                SecondaryTarget = SecondaryPermissionType.AllModules,
                SecondaryTargetName = "*",
                State = action.Value
            });

        if (action.Value)
            await Response().Confirm(strs.aum_enable(Format.Code(user.ToString()))).SendAsync();
        else
            await Response().Confirm(strs.aum_disable(Format.Code(user.ToString()))).SendAsync();
    }

    [Cmd]
    [RequireContext(ContextType.Guild)]
    public async Task AllSrvrMdls(PermissionAction action)
    {
        var newPerm = new Permissionv2
        {
            PrimaryTarget = PrimaryPermissionType.Server,
            PrimaryTargetId = 0,
            SecondaryTarget = SecondaryPermissionType.AllModules,
            SecondaryTargetName = "*",
            State = action.Value
        };

        var allowUser = new Permissionv2
        {
            PrimaryTarget = PrimaryPermissionType.User,
            PrimaryTargetId = ctx.User.Id,
            SecondaryTarget = SecondaryPermissionType.AllModules,
            SecondaryTargetName = "*",
            State = true
        };

        await _service.AddPermissions(ctx.Guild.Id, newPerm, allowUser);

        if (action.Value)
            await Response().Confirm(strs.asm_enable).SendAsync();
        else
            await Response().Confirm(strs.asm_disable).SendAsync();
    }
}