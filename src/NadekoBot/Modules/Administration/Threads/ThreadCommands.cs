#nullable disable
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using System.Text;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class ThreadCommands(AutoThreadService ats) : NadekoModule
    {
        [Cmd]
        [BotPerm(ChannelPermission.CreatePublicThreads)]
        [UserPerm(ChannelPermission.CreatePublicThreads)]
        public async Task ThreadCreate([Leftover] string name)
        {
            if (ctx.Channel is not SocketTextChannel stc)
                return;

            await stc.CreateThreadAsync(name, message: ctx.Message.ReferencedMessage);
            await ctx.OkAsync();
        }

        [Cmd]
        [BotPerm(ChannelPermission.ManageThreads)]
        [UserPerm(ChannelPermission.ManageThreads)]
        public async Task ThreadDelete([Leftover] string name)
        {
            if (ctx.Channel is not SocketTextChannel stc)
                return;

            var t = stc.Threads.FirstOrDefault(
                x => string.Equals(x.Name, name, StringComparison.InvariantCultureIgnoreCase));

            if (t is null)
            {
                await Response().Error(strs.not_found).SendAsync();
                return;
            }

            await t.DeleteAsync();
            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageThreads)]
        [BotPerm(ChannelPermission.CreatePublicThreads)]
        [NadekoOptions<AutoThreadOptions>]
        public async Task AutoThread(params string[] args)
        {
            if (ctx.Channel is not SocketTextChannel stc || stc is SocketThreadChannel)
            {
                await Response().Error(strs.autothread_wrong_channel).SendAsync();
                return;
            }

            if (args.Length == 0 && ats.IsEnabled(stc.Id))
            {
                await ats.DisableAsync(ctx.Guild.Id, stc.Id);
                await Response().Pending(strs.autothread_disabled).SendAsync();
                return;
            }

            var (opts, _) = OptionsParser.ParseFrom(new AutoThreadOptions(), args);

            if (!Enum.TryParse<AutoThreadMode>(opts.Mode, true, out var mode))
            {
                await Response().Error(strs.autothread_invalid_mode).SendAsync();
                return;
            }

            if (!AutoThreadArchive.TryParse(opts.Archive, out var minutes))
            {
                await Response().Error(strs.autothread_invalid_duration).SendAsync();
                return;
            }

            if (!HasArchiveFeature(minutes))
            {
                await Response()
                    .Error(strs.autothread_boost_required(AutoThreadArchive.Pretty(minutes)))
                    .SendAsync();
                return;
            }

            if (opts.Backfill is < 0 or > AutoThreadService.MAX_BACKFILL)
            {
                await Response()
                    .Error(strs.autothread_backfill_invalid(AutoThreadService.MAX_BACKFILL))
                    .SendAsync();
                return;
            }

            await ats.EnableAsync(ctx.Guild.Id, stc.Id, mode, minutes);

            await Response()
                .Confirm(strs.autothread_enabled(Format.Bold(mode.ToString()),
                    Format.Bold(AutoThreadArchive.Pretty(minutes))))
                .SendAsync();

            if (opts.Backfill == 0)
                return;

            var created = await ats.BackfillAsync(stc, mode, opts.Backfill, ctx.Message.Id);
            await Response().Confirm(strs.autothread_backfill_done(created)).SendAsync();
        }

        private bool HasArchiveFeature(int minutes)
            => minutes switch
            {
                AutoThreadArchive.THREE_DAYS
                    => ctx.Guild.Features.HasFeature(GuildFeature.ThreeDayThreadArchive),
                AutoThreadArchive.ONE_WEEK
                    => ctx.Guild.Features.HasFeature(GuildFeature.SevenDayThreadArchive),
                _ => true
            };

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageThreads)]
        public async Task AutoThreadList()
        {
            var items = await ats.GetAllAsync(ctx.Guild.Id);

            if (items.Count == 0)
            {
                await Response().Confirm(strs.autothread_list_none).SendAsync();
                return;
            }

            await Response()
                .Paginated()
                .Items(items)
                .PageSize(10)
                .Page((pageItems, _) =>
                {
                    var sb = new StringBuilder();
                    foreach (var item in pageItems)
                    {
                        sb.AppendLine($"<#{item.ChannelId}> - `{item.Mode}` - "
                                      + $"`{AutoThreadArchive.Pretty(item.ArchiveDurationMinutes)}`");
                    }

                    return CreateEmbed()
                        .WithOkColor()
                        .WithTitle(GetText(strs.autothread_list))
                        .WithDescription(sb.ToString());
                })
                .SendAsync();
        }
    }
}
