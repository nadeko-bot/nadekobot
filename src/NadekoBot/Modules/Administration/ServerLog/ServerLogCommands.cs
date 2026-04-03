using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    [NoPublicBot]
    public partial class LogCommands : NadekoModule<ILogCommandService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogServer(PermissionAction action)
        {
            await _service.LogServer(ctx.Guild.Id, ctx.Channel.Id, action.Value);
            if (action.Value)
                await Response().Confirm(strs.log_all).SendAsync();
            else
                await Response().Confirm(strs.log_disabled).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore()
        {
            var ignores = _service.GetLogIgnores(ctx.Guild.Id);

            var chs = ignores.Where(x => x.ItemType == IgnoredItemType.Channel).ToList();
            var usrs = ignores.Where(x => x.ItemType == IgnoredItemType.User).ToList();
            var cats = ignores.Where(x => x.ItemType == IgnoredItemType.Category).ToList();

            var catNames = new List<string>();
            foreach (var cat in cats)
            {
                var catChannel = await ctx.Guild.GetChannelAsync(cat.LogItemId);
                var name = catChannel?.Name ?? cat.LogItemId.ToString();
                catNames.Add($"{cat.LogItemId} | {name}");
            }

            var eb = CreateEmbed()
                        .WithOkColor()
                        .AddField(GetText(strs.log_ignored_channels),
                            chs.Count == 0
                                ? "-"
                                : string.Join('\n', chs.Select(x => $"{x.LogItemId} | <#{x.LogItemId}>")))
                        .AddField(GetText(strs.log_ignored_users),
                            usrs.Count == 0
                                ? "-"
                                : string.Join('\n', usrs.Select(x => $"{x.LogItemId} | <@{x.LogItemId}>")))
                        .AddField(GetText(strs.log_ignored_categories),
                            cats.Count == 0
                                ? "-"
                                : string.Join('\n', catNames));

            await Response().Embed(eb).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore([Leftover] ITextChannel target)
        {
            var removed = _service.LogIgnore(ctx.Guild.Id, target.Id, IgnoredItemType.Channel);

            if (!removed)
            {
                await Response()
                      .Confirm(
                          strs.log_ignore_chan(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(
                          strs.log_not_ignore_chan(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore([Leftover] ICategoryChannel target)
        {
            var removed = _service.LogIgnore(ctx.Guild.Id, target.Id, IgnoredItemType.Category);

            if (!removed)
            {
                await Response()
                      .Confirm(strs.log_ignore_category(Format.Bold(target.Name + "(" + target.Id + ")")))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.log_not_ignore_category(Format.Bold(target.Name + "(" + target.Id + ")")))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogIgnore([Leftover] IUser target)
        {
            var removed = _service.LogIgnore(ctx.Guild.Id, target.Id, IgnoredItemType.User);

            if (!removed)
            {
                await Response()
                      .Confirm(strs.log_ignore_user(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
            else
            {
                await Response()
                      .Confirm(strs.log_not_ignore_user(Format.Bold(target.Mention + "(" + target.Id + ")")))
                      .SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task LogEvents()
        {
            var str = string.Join("\n",
                Enum.GetNames<LogType>()
                    .Select(x =>
                    {
                        var logType = Enum.Parse<LogType>(x);
                        var val = _service.GetLogChannelId(ctx.Guild.Id, logType);
                        if (val is not null)
                            return $"{Format.Bold(x)} <#{val}>";
                        return Format.Bold(x);
                    }));

            await Response().Confirm(Format.Bold(GetText(strs.log_events)) + "\n" + str).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [OwnerOnly]
        public async Task Log(LogType type)
        {
            var val = _service.Log(ctx.Guild.Id, ctx.Channel.Id, type);

            if (val)
                await Response().Confirm(strs.log(Format.Bold(type.ToString()))).SendAsync();
            else
                await Response().Confirm(strs.log_stop(Format.Bold(type.ToString()))).SendAsync();
        }
    }
}
