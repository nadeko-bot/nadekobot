#nullable disable
using NadekoBot.Modules.Administration.Services;
using System.Text;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    public partial class SelfAssignedRolesHelpers : NadekoModule<SelfAssignedRolesService>
    {
        private readonly SarAssignerService _sas;

        public SelfAssignedRolesHelpers(SarAssignerService sas)
        {
            _sas = sas;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Iam([Leftover] IRole role)
        {
            var guildUser = (IGuildUser)ctx.User;

            var group = await _service.GetRoleGroup(ctx.Guild.Id, role.Id);

            IUserMessage msg = null;
            try
            {
                if (group is null)
                {
                    msg = await Response().Error(strs.self_assign_not).SendAsync();
                    return;
                }

                var tcs = new TaskCompletionSource<SarAssignResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _sas.Add(new()
                {
                    Group = group,
                    RoleId = role.Id,
                    User = guildUser,
                    CompletionTask = tcs
                });

                var res = await tcs.Task;

                if (res.TryPickT0(out _, out var error))
                {
                    msg = await Response()
                                .Confirm(strs.self_assign_success(Format.Bold(role.Name)))
                                .SendAsync();
                }
                else
                {
                    var resStr = error.Match(
                        _ => strs.error_occured,
                        lvlReq => strs.self_assign_not_level(Format.Bold(lvlReq.Level.ToString())),
                        roleRq => strs.self_assign_role_req(Format.Bold(ctx.Guild.GetRole(roleRq.RoleId).ToString()
                                                                        ?? "missing role " + roleRq.RoleId),
                            group.Name),
                        _ => strs.self_assign_already(Format.Bold(role.Name)),
                        _ => strs.self_assign_perms);

                    msg = await Response().Error(resStr).SendAsync();
                }
            }
            finally
            {
                var ad = _service.GetAutoDelete(ctx.Guild.Id);

                if (ad)
                {
                    msg?.DeleteAfter(3);
                    ctx.Message.DeleteAfter(3);
                }
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task Iamnot([Leftover] IRole role)
        {
            var guildUser = (IGuildUser)ctx.User;

            IUserMessage msg = null;
            try
            {
                if (!guildUser.RoleIds.Contains(role.Id))
                {
                    msg = await Response().Error(strs.self_assign_not_have(Format.Bold(role.Name))).SendAsync();
                    return;
                }

                var group = await _service.GetRoleGroup(ctx.Guild.Id, role.Id);

                if (group is null || group.Roles.All(x => x.RoleId != role.Id))
                {
                    msg = await Response().Error(strs.self_assign_not).SendAsync();
                    return;
                }

                if (role.Position >= ((SocketGuild)ctx.Guild).CurrentUser.Roles.Max(x => x.Position))
                {
                    msg = await Response().Error(strs.self_assign_perms).SendAsync();
                    return;
                }

                await guildUser.RemoveRoleAsync(role);
                msg = await Response().Confirm(strs.self_assign_remove(Format.Bold(role.Name))).SendAsync();
            }
            finally
            {
                var ad = _service.GetAutoDelete(ctx.Guild.Id);
                if (ad)
                {
                    msg?.DeleteAfter(3);
                    ctx.Message.DeleteAfter(3);
                }
            }
        }
    }

    [Group("sar")]
    public partial class SelfAssignedRolesCommands : NadekoModule<SelfAssignedRolesService>
    {
        private readonly SarAssignerService _sas;

        public SelfAssignedRolesCommands(SarAssignerService sas)
        {
            _sas = sas;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [BotPerm(GuildPerm.ManageMessages)]
        public async Task SarAutoDelete()
        {
            var newVal = await _service.ToggleAutoDelete(ctx.Guild.Id);

            if (newVal)
                await Response().Confirm(strs.adsarm_enable(prefix)).SendAsync();
            else
                await Response().Confirm(strs.adsarm_disable(prefix)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Priority(1)]
        public Task SarAdd([Leftover] IRole role)
            => SarAdd(0, role);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Priority(0)]
        public async Task SarAdd(int group, [Leftover] IRole role)
        {
            if (!await CheckRoleHierarchy(role))
                return;

            await _service.AddAsync(ctx.Guild.Id, role.Id, group);

            await Response()
                  .Confirm(strs.role_added(Format.Bold(role.Name), Format.Bold(group.ToString())))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Priority(0)]
        public async Task SarGroupName(int group, [Leftover] string name = null)
        {
            var set = await _service.SetGroupNameAsync(ctx.Guild.Id, group, name);

            if (set)
            {
                await Response()
                      .Confirm(strs.group_name_added(Format.Bold(group.ToString()), Format.Bold(name)))
                      .SendAsync();
            }
            else
            {
                await Response().Confirm(strs.group_name_removed(Format.Bold(group.ToString()))).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [Priority(1)]
        public Task SarRemove([Leftover] IRole role)
            => SarRemove(role.Id);
        
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [Priority(0)]
        public async Task SarRemove([Leftover] ulong roleId)
        {
            var role = await ctx.Guild.GetRoleAsync(roleId);
            var success = await _service.RemoveAsync(ctx.Guild.Id, roleId);
            if (!success)
                await Response().Error(strs.self_assign_not).SendAsync();
            else
                await Response().Confirm(strs.self_assign_rem(Format.Bold(role?.Name ?? roleId.ToString()))).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task SarList(int page = 1)
        {
            if (--page < 0)
                return;

            var groups = await _service.GetSarsAsync(ctx.Guild.Id);

            var gDict = groups.ToDictionary(x => x.Id, x => x);

            await Response()
                  .Paginated()
                  .Items(groups.SelectMany(x => x.Roles).ToList())
                  .PageSize(20)
                  .CurrentPage(page)
                  .Page(async (items, _) =>
                  {
                      var roleGroups = items
                                       .GroupBy(x => x.SarGroupId)
                                       .OrderBy(x => x.Key);

                      var eb = CreateEmbed()
                                      .WithOkColor()
                                      .WithTitle(GetText(strs.self_assign_list(groups.Sum(x => x.Roles.Count))));

                      foreach (var kvp in roleGroups)
                      {
                          var group = gDict[kvp.Key];

                          var groupNameText = "";

                          if (!string.IsNullOrWhiteSpace(group.Name))
                              groupNameText += $"  **{group.Name}**";

                          groupNameText = $"`{group.GroupNumber}`  {groupNameText}";

                          var rolesStr = new StringBuilder();

                          if (group.IsExclusive)
                          {
                              rolesStr.AppendLine(Format.Italics(GetText(strs.choose_one)));
                          }

                          if (group.RoleReq is ulong rrId)
                          {
                              var rr = ctx.Guild.GetRole(rrId);

                              if (rr is null)
                              {
                                  await _service.SetGroupRoleReq(group.GuildId, group.GroupNumber, null);
                              }
                              else
                              {
                                  rolesStr.AppendLine(
                                      Format.Italics(GetText(strs.requires_role(Format.Bold(rr.Name)))));
                              }
                          }

                          foreach (var sar in kvp)
                          {
                              var roleName = (ctx.Guild.GetRole(sar.RoleId)?.Name ?? (sar.RoleId + " (deleted)"));
                              rolesStr.Append("- " + Format.Code(roleName));

                              if (sar.LevelReq > 0)
                              {
                                  rolesStr.Append($"  *[lvl {sar.LevelReq}+]*");
                              }

                              rolesStr.AppendLine();
                          }


                          eb.AddField(groupNameText, rolesStr, false);
                      }

                      return eb;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task SarExclusive(int groupNumber)
        {
            var areExclusive = await _service.SetGroupExclusivityAsync(ctx.Guild.Id, groupNumber);

            if (areExclusive is null)
            {
                await Response().Error(strs.sar_group_not_found).SendAsync();
                return;
            }
            
            if (areExclusive is true)
            {
                await Response().Confirm(strs.self_assign_excl).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.self_assign_no_excl).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task SarRoleLevelReq(int level, [Leftover] IRole role)
        {
            if (level < 0)
                return;

            var succ = await _service.SetRoleLevelReq(ctx.Guild.Id, role.Id, level);

            if (!succ)
            {
                await Response().Error(strs.self_assign_not).SendAsync();
                return;
            }

            await Response()
                  .Confirm(strs.self_assign_level_req(Format.Bold(role.Name),
                      Format.Bold(level.ToString())))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task SarGroupRoleReq(int groupNumber, [Leftover] IRole role)
        {
            var succ = await _service.SetGroupRoleReq(ctx.Guild.Id, groupNumber, role.Id);

            if (!succ)
            {
                await Response().Error(strs.sar_group_not_found).SendAsync();
                return;
            }

            await Response()
                  .Confirm(strs.self_assign_group_role_req(
                      Format.Bold(groupNumber.ToString()),
                      Format.Bold(role.Name)))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        public async Task SarGroupDelete(int groupNumber)
        {
            var succ = await _service.DeleteRoleGroup(ctx.Guild.Id, groupNumber);
            if (succ)
                await Response().Confirm(strs.sar_group_deleted(Format.Bold(groupNumber.ToString()))).SendAsync();
            else
                await Response().Error(strs.sar_group_not_found).SendAsync();
        }
    }
}