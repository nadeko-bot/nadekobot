#nullable disable
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class InviteCommands : NadekoModule<InviteService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.CreateInstantInvite)]
        [UserPerm(ChannelPerm.CreateInstantInvite)]
        [NadekoOptions<InviteService.Options>]
        public async Task InviteCreate(params string[] args)
        {
            var (opts, success) = OptionsParser.ParseFrom(new InviteService.Options(), args);
            if (!success)
                return;

            var ch = (ITextChannel)ctx.Channel;
            var invite = await ch.CreateInviteAsync(opts.Expire, opts.MaxUses, opts.Temporary, opts.Unique);

            await Response().Confirm($"{ctx.User.Mention} https://discord.gg/{invite.Code}").SendAsync();
        }

        private const int INVITES_PER_PAGE = 9;

        private static async Task<List<IInviteMetadata>> GetOrderedInvitesAsync(ITextChannel channel)
        {
            var invites = (await channel.GetInvitesAsync()).ToList();
            invites.Sort(static (a, b) =>
            {
                var byDate = Nullable.Compare(a.CreatedAt, b.CreatedAt);
                return byDate != 0 ? byDate : string.CompareOrdinal(a.Code, b.Code);
            });
            return invites;
        }

        // [UserPerm] and [BotPerm] check only the channel where the command runs
        private async Task<bool> CanManageInvitesInAsync(ITextChannel ch)
        {
            if (ch is null)
                return true;

            if (!((IGuildUser)ctx.User).GetPermissions(ch).ManageChannel)
            {
                await Response().Error(strs.insuf_perms_u).SendAsync();
                return false;
            }

            if (!((ctx.Guild as SocketGuild)?.CurrentUser.GetPermissions(ch).ManageChannel ?? false))
            {
                await Response().Error(strs.insuf_perms_i).SendAsync();
                return false;
            }

            return true;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.ManageChannels)]
        [UserPerm(ChannelPerm.ManageChannels)]
        public async Task InviteList(int page = 1, [Leftover] ITextChannel ch = null)
        {
            if (--page < 0)
                return;

            if (!await CanManageInvitesInAsync(ch))
                return;

            var channel = ch ?? (ITextChannel)ctx.Channel;
            var invites = await GetOrderedInvitesAsync(channel);

            var lastPage = Math.Max(0, (invites.Count - 1) / INVITES_PER_PAGE);

            await Response()
                  .Paginated()
                  .Items(invites)
                  .PageSize(INVITES_PER_PAGE)
                  .CurrentPage(Math.Min(page, lastPage))
                  .Page((invs, curPage) =>
                  {
                      if (!invs.Any())
                          return CreateEmbed().WithErrorColor().WithDescription(GetText(strs.no_invites));

                      var i = curPage * INVITES_PER_PAGE + 1;
                      var embed = CreateEmbed().WithOkColor();
                      foreach (var inv in invs)
                      {
                          var expiryString = inv.MaxAge is null or 0 || inv.CreatedAt is null
                              ? "∞"
                              : (inv.CreatedAt.Value.AddSeconds(inv.MaxAge.Value).UtcDateTime - DateTime.UtcNow)
                              .ToString(
                                  """d\.hh\:mm\:ss""");
                          var creator = inv.Inviter?.ToString().TrimTo(25) ?? "?";
                          var usesString = $"{inv.Uses} / {(inv.MaxUses is null or 0 ? "∞" : inv.MaxUses.ToString())}";

                          var desc = $@"`{GetText(strs.inv_uses)}` **{usesString}**
`{GetText(strs.inv_expire)}` **{expiryString}**
                        
{inv.Url} ";
                          embed.AddField($"#{i++} {creator}", desc);
                      }

                      return embed;
                  })
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.ManageChannels)]
        [UserPerm(ChannelPerm.ManageChannels)]
        public async Task InviteDelete(int index, [Leftover] ITextChannel ch = null)
        {
            if (!await CanManageInvitesInAsync(ch))
                return;

            var channel = ch ?? (ITextChannel)ctx.Channel;
            var invites = await GetOrderedInvitesAsync(channel);

            if (index < 1 || index > invites.Count)
            {
                await Response().Error(strs.invite_not_found(prefix)).SendAsync();
                return;
            }

            var inv = invites[index - 1];
            await inv.DeleteAsync();

            await Response().Confirm(strs.invite_deleted(Format.Bold(inv.Code))).SendAsync();
        }
    }
}