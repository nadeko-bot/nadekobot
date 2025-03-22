using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp;

public partial class Xp
{
    [RequireUserPermission(GuildPermission.Administrator)]
    public class XpExclusionCommands : NadekoModule<XpExclusionService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task XpExclusion()
        {
            var exclusions = await _service.GetExclusionsAsync(ctx.Guild.Id);

            if (!exclusions.Any())
            {
                await Response().Pending(strs.xp_exclusion_none).SendAsync();
                return;
            }

            await Response()
                .Paginated()
                .Items(exclusions.OrderBy(x => x.ItemType).ToList())
                .PageSize(10)
                .Page((items, _) =>
                {
                    var eb = CreateEmbed()
                        .WithOkColor()
                        .WithTitle(GetText(strs.xp_exclusion_title));

                    foreach (var item in items)
                    {
                        var itemType = item.ItemType;
                        var mention = GetMention(itemType, item.ItemId);

                        eb.AddField(itemType.ToString(), mention);
                    }

                    return eb;
                })
                .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task XpExclude([Leftover] IRole role)
            => await XpExclude(XpExcludedItemType.Role, role.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task XpExclude([Leftover] IUser user)
            => await XpExclude(XpExcludedItemType.User, user.Id);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task XpExclude(XpExcludedItemType type, ulong itemId)
        {
            var isExcluded = await _service.ToggleExclusionAsync(ctx.Guild.Id, type, itemId);

            if (isExcluded)
                await Response()
                    .Confirm(strs.xp_exclude_added(type.ToString(), GetMention(type, itemId)))
                    .SendAsync();
            else
                await Response()
                    .Confirm(strs.xp_exclude_removed(type.ToString(), GetMention(type, itemId)))
                    .SendAsync();
        }

        private string GetMention(XpExcludedItemType itemType, ulong itemId)
            => itemType switch
            {
                XpExcludedItemType.Role => ctx.Guild.GetRole(itemId)?.ToString() ?? itemId.ToString(),
                XpExcludedItemType.User => (ctx.Guild as SocketGuild)?.GetUser(itemId)?.ToString() ??
                                           itemId.ToString(),
                _ => itemId.ToString()
            };
    }
}