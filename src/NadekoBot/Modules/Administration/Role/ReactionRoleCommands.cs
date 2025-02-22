using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    public partial class ReactionRoleCommands : NadekoModule
    {
        private readonly IReactionRoleService _rero;

        public ReactionRoleCommands(IReactionRoleService rero)
        {
            _rero = rero;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task ReRoAdd(
            ulong messageId,
            string emoteStr,
            IRole role,
            int group = 0,
            int levelReq = 0)
        {
            if (group < 0)
                return;

            if (levelReq < 0)
                return;

            var msg = await ctx.Channel.GetMessageAsync(messageId);
            if (msg is null)
            {
                await Response().Error(strs.rero_message_not_found).SendAsync();
                return;
            }

            if (ctx.User.Id != ctx.Guild.OwnerId
                && ((IGuildUser)ctx.User).GetRoles().Max(x => x.Position) <= role.Position)
            {
                await Response().Error(strs.hierarchy).SendAsync();
                return;
            }

            var emote = emoteStr.ToIEmote();
            await msg.AddReactionAsync(emote);
            var res = await _rero.AddReactionRole(ctx.Guild,
                msg,
                emoteStr,
                role,
                group,
                levelReq);

            await res.Match(
                _ => ctx.OkAsync(),
                async fl =>
                {
                    _ = msg.RemoveReactionAsync(emote, ctx.Client.CurrentUser);
                    await Response().Pending(strs.feature_limit_reached_owner).SendAsync();
                });
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task ReRoList(int page = 1)
        {
            if (--page < 0)
                return;

            var allReros = await _rero.GetReactionRolesAsync(ctx.Guild.Id);

            await Response()
                  .Paginated()
                  .Items(allReros.OrderBy(x => x.Group).ToList())
                  .PageSize(10)
                  .CurrentPage(page)
                  .Page((items, _) =>
                  {
                      var embed = CreateEmbed()
                          .WithOkColor();

                      var content = string.Empty;
                      foreach (var g in items
                                        .GroupBy(x => x.MessageId)
                                        .OrderBy(x => x.Key))
                      {
                          var messageId = g.Key;
                          content +=
                              $"[{messageId}](https://discord.com/channels/{ctx.Guild.Id}/{g.First().ChannelId}/{g.Key})\n";

                          var groupGroups = g.GroupBy(x => x.Group);

                          foreach (var ggs in groupGroups)
                          {
                              content += $"`< {(g.Key == 0 ? ("Not Exclusive (Group 0)") : ($"Group {ggs.Key}"))} >`\n";

                              foreach (var rero in ggs)
                              {
                                  content +=
                                      $"\t{rero.Emote} -> {(ctx.Guild.GetRole(rero.RoleId)?.Mention ?? "<missing role>")}";
                                  if (rero.LevelReq > 0)
                                      content += $" (lvl {rero.LevelReq}+)";
                                  content += '\n';
                              }
                          }
                      }

                      embed.WithDescription(string.IsNullOrWhiteSpace(content)
                          ? "There are no reaction roles on this server"
                          : content);

                      return embed;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task ReRoRemove(ulong messageId)
        {
            var succ = await _rero.RemoveReactionRoles(ctx.Guild.Id, messageId);
            if (succ)
                await ctx.OkAsync();
            else
                await ctx.ErrorAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        public async Task ReRoDeleteAll()
        {
            await _rero.RemoveAllReactionRoles(ctx.Guild.Id);
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageRoles)]
        [BotPerm(GuildPerm.ManageRoles)]
        [Ratelimit(60)]
        public async Task ReRoTransfer(ulong fromMessageId, ulong toMessageId)
        {
            var msg = await ctx.Channel.GetMessageAsync(toMessageId);

            if (msg is null)
            {
                await ctx.ErrorAsync();
                return;
            }

            var reactions = await _rero.TransferReactionRolesAsync(ctx.Guild.Id, fromMessageId, toMessageId);

            if (reactions.Count == 0)
            {
                await ctx.ErrorAsync();
            }
            else
            {
                foreach (var r in reactions)
                {
                    await msg.AddReactionAsync(r);
                }
            }
        }
    }
}