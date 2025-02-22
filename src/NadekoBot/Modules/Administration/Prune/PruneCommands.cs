#nullable disable
using CommandLine;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class PruneCommands : NadekoModule<PruneService>
    {
        private static readonly TimeSpan _twoWeeks = TimeSpan.FromDays(14);

        public sealed class PruneOptions : INadekoCommandOptions
        {
            [Option(shortName: 's',
                longName: "safe",
                Default = false,
                HelpText = "Whether pinned messages should be deleted.",
                Required = false)]
            public bool Safe { get; set; }

            [Option(shortName: 'a',
                longName: "after",
                Default = null,
                HelpText = "Prune only messages after the specified message ID.",
                Required = false)]
            public ulong? After { get; set; }

            public void NormalizeOptions()
            {
            }
        }

        [Cmd]
        [RequireContext(ContextType.DM)]
        [NadekoOptions<PruneOptions>]
        public async Task Prune()
        {
            var progressMsg = await Response().Pending(strs.prune_progress(0, 100)).SendAsync();
            var progress = GetProgressTracker(progressMsg);

            var result = await _service.PruneWhere(ctx.Channel,
                100,
                x => x.Author.Id == ctx.Client.CurrentUser.Id,
                progress);

            ctx.Message.DeleteAfter(3);

            await SendResult(result);
            await progressMsg.DeleteAsync();
        }

        //deletes her own messages, no perm required
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<PruneOptions>]
        public async Task Prune(params string[] args)
        {
            var (opts, _) = OptionsParser.ParseFrom(new PruneOptions(), args);

            var user = await ctx.Guild.GetCurrentUserAsync();

            var progressMsg = await Response().Pending(strs.prune_progress(0, 100)).SendAsync();
            var progress = GetProgressTracker(progressMsg);

            PruneResult result;
            if (opts.Safe)
                result = await _service.PruneWhere((ITextChannel)ctx.Channel,
                    100,
                    x => x.Author.Id == user.Id && !x.IsPinned,
                    progress,
                    opts.After);
            else
                result = await _service.PruneWhere((ITextChannel)ctx.Channel,
                    100,
                    x => x.Author.Id == user.Id,
                    progress,
                    opts.After);

            ctx.Message.DeleteAfter(3);

            await SendResult(result);
            await progressMsg.DeleteAsync();
        }

        // prune x
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        [NadekoOptions<PruneOptions>]
        [Priority(1)]
        public async Task Prune(int count, params string[] args)
        {
            count++;
            if (count < 1)
                return;

            if (count > 1000)
                count = 1000;

            var (opts, _) = OptionsParser.ParseFrom<PruneOptions>(new PruneOptions(), args);

            var progressMsg = await Response().Pending(strs.prune_progress(0, count)).SendAsync();
            var progress = GetProgressTracker(progressMsg);

            PruneResult result;
            if (opts.Safe)
                result = await _service.PruneWhere((ITextChannel)ctx.Channel,
                    count,
                    x => !x.IsPinned && x.Id != progressMsg.Id,
                    progress,
                    opts.After);
            else
                result = await _service.PruneWhere((ITextChannel)ctx.Channel,
                    count,
                    x => x.Id != progressMsg.Id,
                    progress,
                    opts.After);

            await SendResult(result);
            await progressMsg.DeleteAsync();
        }

        private IProgress<(int, int)> GetProgressTracker(IUserMessage progressMsg)
        {
            var progress = new Progress<(int, int)>(async (x) =>
            {
                var (deleted, total) = x;
                try
                {
                    await progressMsg.ModifyAsync(props =>
                    {
                        props.Embed = CreateEmbed()
                                      .WithPendingColor()
                                      .WithDescription(GetText(strs.prune_progress(deleted, total)))
                                      .Build();
                    });
                }
                catch
                {
                }
            });

            return progress;
        }

        //prune @user [x]
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        [NadekoOptions<PruneOptions>]
        [Priority(0)]
        public Task Prune(IGuildUser user, int count = 100, params string[] args)
            => Prune(user.Id, count, args);

        //prune userid [x]
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        [NadekoOptions<PruneOptions>]
        [Priority(0)]
        public async Task Prune(ulong userId, int count = 100, params string[] args)
        {
            if (userId == ctx.User.Id)
                count++;

            if (count < 1)
                return;

            if (count > 1000)
                count = 1000;

            var (opts, _) = OptionsParser.ParseFrom<PruneOptions>(new PruneOptions(), args);

            var progressMsg = await Response().Pending(strs.prune_progress(0, count)).SendAsync();
            var progress = GetProgressTracker(progressMsg);

            PruneResult result;
            if (opts.Safe)
            {
                result = await _service.PruneWhere((ITextChannel)ctx.Channel,
                    count,
                    m => m.Author.Id == userId && DateTime.UtcNow - m.CreatedAt < _twoWeeks && !m.IsPinned,
                    progress,
                    opts.After
                );
            }
            else
            {
                result = await _service.PruneWhere((ITextChannel)ctx.Channel,
                    count,
                    m => m.Author.Id == userId && DateTime.UtcNow - m.CreatedAt < _twoWeeks,
                    progress,
                    opts.After
                );
            }

            await SendResult(result);
            await progressMsg.DeleteAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        public async Task PruneCancel()
        {
            var ok = await _service.CancelAsync(ctx.Guild.Id);

            if (!ok)
            {
                await Response().Error(strs.prune_not_found).SendAsync();
                return;
            }


            await Response().Confirm(strs.prune_cancelled).SendAsync();
        }


        private async Task SendResult(PruneResult result)
        {
            switch (result)
            {
                case PruneResult.Success:
                    break;
                case PruneResult.AlreadyRunning:
                    var msg = await Response().Pending(strs.prune_already_running).SendAsync();
                    msg.DeleteAfter(5);
                    break;
                case PruneResult.FeatureLimit:
                    var msg2 = await Response().Pending(strs.feature_limit_reached_owner).SendAsync();
                    msg2.DeleteAfter(10);
                    break;
                default:
                    Log.Error("Unhandled result received in prune: {Result}", result);
                    await Response().Error(strs.error_occured).SendAsync();
                    break;
            }
        }
    }
}