using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using System.Text;
using ContextType = Discord.Commands.ContextType;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group("btr")]
    public partial class ButtonRoleCommands : NadekoModule<ButtonRolesService>
    {
        private List<ActionRowBuilder> GetActionRows(IReadOnlyList<ButtonRole> roles)
        {
            var rows = roles.Select((x, i) => (Index: i, ButtonRole: x))
                            .GroupBy(x => x.Index / 5)
                            .Select(x => x.Select(y => y.ButtonRole))
                            .Select(x =>
                            {
                                var ab = new ActionRowBuilder()
                                    .WithComponents(x.Select(y =>
                                                     {
                                                         var curRole = ctx.Guild.GetRole(y.RoleId);
                                                         var label = string.IsNullOrWhiteSpace(y.Label)
                                                             ? curRole?.ToString() ?? "?missing " + y.RoleId
                                                             : y.Label;

                                                         var btnEmote = EmoteTypeReader.TryParse(y.Emote, out var e)
                                                             ? e
                                                             : null;

                                                         return new ButtonBuilder()
                                                                .WithCustomId(y.ButtonId)
                                                                .WithEmote(btnEmote)
                                                                .WithLabel(label)
                                                                .WithStyle(ButtonStyle.Secondary)
                                                                .Build() as IMessageComponent;
                                                     })
                                                     .ToList());

                                return ab;
                            })
                            .ToList();
            return rows;
        }

        private async Task<MessageLink?> CreateMessageLinkAsync(ulong messageId)
        {
            var msg = await ctx.Channel.GetMessageAsync(messageId);
            if (msg is null)
                return null;

            return new MessageLink(ctx.Guild, ctx.Channel, msg);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public async Task BtnRoleAdd(ulong messageId, IEmote emote, [Leftover] IRole role)
        {
            var link = await CreateMessageLinkAsync(messageId);

            if (link is null)
            {
                await Response().Error(strs.invalid_message_id).SendAsync();
                return;
            }

            await BtnRoleAdd(link, emote, role);
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public async Task BtnRoleAdd(MessageLink link, IEmote emote, [Leftover] IRole role)
        {
            if (link.Message is not IUserMessage msg || !msg.IsAuthor(ctx.Client))
            {
                await Response().Error(strs.invalid_message_link).SendAsync();
                return;
            }

            if (!await CheckRoleHierarchy(role))
            {
                await Response().Error(strs.hierarchy).SendAsync();
                return;
            }

            var success = await _service.AddButtonRole(ctx.Guild.Id, link.Channel.Id, role.Id, link.Message.Id, emote);
            if (!success)
            {
                await Response().Error(strs.btnrole_message_max).SendAsync();
                return;
            }

            var roles = await _service.GetButtonRoles(ctx.Guild.Id, link.Message.Id);

            var rows = GetActionRows(roles);

            await msg.ModifyAsync(x => x.Components = new(new ComponentBuilder().WithRows(rows).Build()));
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public Task BtnRoleRemove(ulong messageId, IRole role)
            => BtnRoleRemove(messageId, role.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public Task BtnRoleRemove(MessageLink link, IRole role)
            => BtnRoleRemove(link.Message.Id, role.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public Task BtnRoleRemove(MessageLink link, ulong roleId)
            => BtnRoleRemove(link.Message.Id, roleId);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public async Task BtnRoleRemove(ulong messageId, ulong roleId)
        {
            var removed = await _service.RemoveButtonRole(ctx.Guild.Id, messageId, roleId);
            if (removed is null)
            {
                await Response().Error(strs.btnrole_not_found).SendAsync();
                return;
            }

            var roles = await _service.GetButtonRoles(ctx.Guild.Id, messageId);

            var ch = await ctx.Guild.GetTextChannelAsync(removed.ChannelId);

            if (ch is null)
            {
                await Response().Error(strs.btnrole_removeall_not_found).SendAsync();
                return;
            }

            var msg = await ch.GetMessageAsync(removed.MessageId) as IUserMessage;

            if (msg is null)
            {
                await Response().Error(strs.btnrole_removeall_not_found).SendAsync();
                return;
            }

            var rows = GetActionRows(roles);
            await msg.ModifyAsync(x => x.Components = new(new ComponentBuilder().WithRows(rows).Build()));
            await Response().Confirm(strs.btnrole_removed).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public Task BtnRoleRemoveAll(MessageLink link)
            => BtnRoleRemoveAll(link.Message.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public async Task BtnRoleRemoveAll(ulong messageId)
        {
            var succ = await _service.RemoveButtonRoles(ctx.Guild.Id, messageId);

            if (succ.Count == 0)
            {
                await Response().Error(strs.btnrole_not_found).SendAsync();
                return;
            }

            var info = succ[0];

            var ch = await ctx.Guild.GetTextChannelAsync(info.ChannelId);
            if (ch is null)
            {
                await Response().Pending(strs.btnrole_removeall_not_found).SendAsync();
                return;
            }

            var msg = await ch.GetMessageAsync(info.MessageId) as IUserMessage;
            if (msg is null)
            {
                await Response().Pending(strs.btnrole_removeall_not_found).SendAsync();
                return;
            }

            await msg.ModifyAsync(x => x.Components = new(new ComponentBuilder().Build()));
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public async Task BtnRoleList()
        {
            var btnRoles = await _service.GetButtonRoles(ctx.Guild.Id, null);

            var groups = btnRoles
                         .GroupBy(x => (x.ChannelId, x.MessageId))
                         .ToList();

            await Response()
                  .Paginated()
                  .Items(groups)
                  .PageSize(1)
                  .AddFooter(false)
                  .Page(async (items, page) =>
                  {
                      var eb = CreateEmbed()
                          .WithOkColor();

                      var item = items.FirstOrDefault();
                      if (item == default)
                      {
                          eb.WithPendingColor()
                            .WithDescription(GetText(strs.btnrole_none));

                          return eb;
                      }

                      var (cid, msgId) = item.Key;

                      var str = new StringBuilder();

                      var ch = await ctx.Client.GetChannelAsync(cid) as IMessageChannel;

                      str.AppendLine($"Channel: {ch?.ToString() ?? cid.ToString()}");
                      str.AppendLine($"Message: {msgId}");

                      if (ch is not null)
                      {
                          var msg = await ch.GetMessageAsync(msgId);
                          if (msg is not null)
                          {
                              str.AppendLine(new MessageLink(ctx.Guild, ch, msg).ToString());
                          }
                      }

                      str.AppendLine("---");

                      foreach (var x in item.AsEnumerable())
                      {
                          var role = ctx.Guild.GetRole(x.RoleId);

                          str.AppendLine($"{x.Emote}  {(role?.ToString() ?? x.RoleId.ToString())}");
                      }

                      eb.WithDescription(str.ToString());

                      return eb;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public Task BtnRoleExclusive(MessageLink link, PermissionAction exclusive)
            => BtnRoleExclusive(link.Message.Id, exclusive);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(GuildPerm.ManageRoles)]
        [RequireUserPermission(GuildPerm.ManageRoles)]
        public async Task BtnRoleExclusive(ulong messageId, PermissionAction exclusive)
        {
            var res = await _service.SetExclusiveButtonRoles(ctx.Guild.Id, messageId, exclusive.Value);

            if (!res)
            {
                await Response().Error(strs.btnrole_not_found).SendAsync();
                return;
            }

            if (exclusive.Value)
            {
                await Response().Confirm(strs.btnrole_exclusive).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.btnrole_multiple).SendAsync();
            }
        }
    }
}