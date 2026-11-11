using System.Text;

namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    public sealed class ReputationCommands : NadekoModule<ReputationService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.EmbedLinks)]
        public async Task Rep()
        {
            var info = await _service.GetAsync(ctx.Guild.Id, ctx.User.Id);

            var next = info.NextGiveAt is DateTime nextAt
                ? GetText(strs.rep_cooldown(TimestampTag.FromDateTime(nextAt, TimestampTagStyles.Relative)))
                : GetText(strs.rep_ready);

            await Response()
                  .Confirm(GetText(strs.rep_show(Format.Bold(info.Reputation.ToString("N0", Culture)))) + "\n" + next)
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.EmbedLinks)]
        public async Task Rep([Leftover] IGuildUser user)
        {
            if (user.Id == ctx.User.Id)
            {
                await Response().Error(strs.rep_self).SendAsync();
                return;
            }

            if (user.IsBot)
            {
                await Response().Error(strs.rep_bot).SendAsync();
                return;
            }

            var result = await _service.GiveAsync(ctx.Guild.Id, ctx.User.Id, user.Id);
            if (!result.Given)
            {
                await Response()
                      .Error(strs.rep_cooldown(TimestampTag.FromDateTime(result.NextGiveAt, TimestampTagStyles.Relative)))
                      .SendAsync();
                return;
            }

            await Response()
                  .Confirm(strs.rep_given(user.Mention, Format.Bold(result.Reputation.ToString("N0", Culture))))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [BotPerm(ChannelPerm.EmbedLinks)]
        public async Task RepLeaderboard(int page = 1)
        {
            if (--page < 0)
                return;

            var total = await _service.CountLeaderboardAsync(ctx.Guild.Id);
            if (total == 0)
            {
                await Response().Error(strs.rep_lb_empty).SendAsync();
                return;
            }

            var lastPage = (total - 1) / ReputationService.LB_PAGE_SIZE;
            await Response()
                  .Paginated()
                  .PageItems(p => _service.GetLeaderboardAsync(ctx.Guild.Id, p))
                  .TotalElements(total)
                  .PageSize(ReputationService.LB_PAGE_SIZE)
                  .CurrentPage(Math.Min(page, lastPage))
                  .Page((items, curPage) =>
                  {
                      var eb = CreateEmbed()
                               .WithOkColor()
                               .WithTitle(GetText(strs.rep_lb));

                      var sb = new StringBuilder();
                      var rank = (curPage * ReputationService.LB_PAGE_SIZE) + 1;
                      foreach (var item in items)
                      {
                          sb.Append('#')
                            .Append(rank++)
                            .Append(" <@")
                            .Append(item.UserId)
                            .Append("> - ")
                            .Append(item.Reputation.ToString("N0", Culture))
                            .Append('\n');
                      }

                      return eb.WithDescription(sb.ToString());
                  })
                  .SendAsync();
        }
    }
}
