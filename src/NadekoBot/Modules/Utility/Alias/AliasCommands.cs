#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class CommandMapCommands : NadekoModule<AliasService>
    {
        private readonly DbService _db;
        private readonly DiscordSocketClient _client;

        public CommandMapCommands(DbService db, DiscordSocketClient client)
        {
            _db = db;
            _client = client;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AliasesClear()
        {
            var count = await _service.ClearAliases(ctx.Guild.Id);
            await Response().Confirm(strs.aliases_cleared(count)).SendAsync();
        }

        [Cmd]
        [UserPerm(GuildPerm.Administrator)]
        [RequireContext(ContextType.Guild)]
        public async Task Alias(string trigger, [Leftover] string mapping = null)
        {
            if (string.IsNullOrWhiteSpace(trigger))
                return;

            trigger = trigger.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(mapping))
            {
                if (!await _service.RemoveAliasAsync(ctx.Guild.Id, trigger))
                {
                    await Response().Error(strs.alias_remove_fail(Format.Code(trigger))).SendAsync();
                    return;
                }

                await Response().Confirm(strs.alias_removed(Format.Code(trigger))).SendAsync();
                return;
            }

            await _service.AddAliasAsync(ctx.Guild.Id, trigger, mapping);

            await Response().Confirm(strs.alias_added(Format.Code(trigger), Format.Code(mapping))).SendAsync();
        }


        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task AliasList(int page = 1)
        {
            page -= 1;

            if (page < 0)
                return;

            var aliases = await _service.GetAliasesAsync(ctx.Guild.Id);
            if (aliases is null || aliases.Count == 0)
            {
                await Response().Error(strs.aliases_none).SendAsync();
                return;
            }

            var arr = aliases.Select(x => (Trigger: x.Key, Mapping: x.Value)).ToArray();

            await Response()
                  .Paginated()
                  .Items(arr)
                  .PageSize(10)
                  .CurrentPage(page)
                  .Page((items, _) =>
                  {
                      return CreateEmbed()
                             .WithOkColor()
                             .WithTitle(GetText(strs.alias_list))
                             .WithDescription(string.Join("\n", items.Select(x => $"`{x.Trigger}` => `{x.Mapping}`")));
                  })
                  .SendAsync();
        }
    }
}