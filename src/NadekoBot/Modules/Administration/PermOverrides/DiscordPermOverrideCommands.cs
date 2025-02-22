#nullable disable
using NadekoBot.Common.TypeReaders;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class DiscordPermOverrideCommands : NadekoModule<DiscordPermOverrideService>
    {
        // override stats, it should require that the user has managessages guild permission
        // .po 'stats' add user guild managemessages
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task DiscordPermOverride(CommandOrExprInfo cmd, params GuildPerm[] perms)
        {
            if (perms is null || perms.Length == 0)
            {
                await _service.RemoveOverride(ctx.Guild.Id, cmd.Name);
                await Response().Confirm(strs.perm_override_reset).SendAsync();
                return;
            }

            var aggregatePerms = perms.Aggregate((acc, seed) => seed | acc);
            await _service.AddOverride(ctx.Guild.Id, cmd.Name, aggregatePerms);

            await Response()
                  .Confirm(strs.perm_override(Format.Bold(aggregatePerms.ToString()),
                      Format.Code(cmd.Name)))
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task DiscordPermOverrideReset()
        {
            var result = await PromptUserConfirmAsync(CreateEmbed()
                                                      .WithOkColor()
                                                      .WithDescription(GetText(strs.perm_override_all_confirm)));

            if (!result)
                return;

            await _service.ClearAllOverrides(ctx.Guild.Id);

            await Response().Confirm(strs.perm_override_all).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task DiscordPermOverrideList(int page = 1)
        {
            if (--page < 0)
                return;

            var allOverrides = await _service.GetAllOverrides(ctx.Guild.Id);

            await Response()
                  .Paginated()
                  .Items(allOverrides)
                  .PageSize(9)
                  .CurrentPage(page)
                  .Page((items, _) =>
                  {
                      var eb = CreateEmbed().WithTitle(GetText(strs.perm_overrides)).WithOkColor();

                      if (items.Count == 0)
                          eb.WithDescription(GetText(strs.perm_override_page_none));
                      else
                      {
                          eb.WithDescription(items.Select(ov => $"{ov.Command} => {ov.Perm.ToString()}")
                                                  .Join("\n"));
                      }

                      return eb;
                  })
                  .SendAsync();
        }
    }
}