#nullable disable
using CommandLine;
using NadekoBot.Modules.Administration.Services;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class PruneCommands : NadekoModule<PruneService>
    {
        private const int MAX_PRUNE_COUNT = 1000;
        private const int DEFAULT_PRUNE_COUNT = 100;

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
        public Task Prune()
        {
            var botId = ctx.Client.CurrentUser.Id;
            return RunPruneAsync(DEFAULT_PRUNE_COUNT, m => m.Author.Id == botId, null);
        }

        //deletes her own messages, no perm required
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [NadekoOptions<PruneOptions>]
        public async Task Prune(params string[] args)
        {
            var (opts, _) = OptionsParser.ParseFrom(new PruneOptions(), args);

            var user = await ctx.Guild.GetCurrentUserAsync();

            ctx.Message.DeleteAfter(3);

            await RunPruneAsync(DEFAULT_PRUNE_COUNT,
                opts.Safe
                    ? m => m.Author.Id == user.Id && !m.IsPinned
                    : m => m.Author.Id == user.Id,
                opts.After);
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
            // the command message itself is also deleted
            count++;

            if (count < 1)
                return;

            if (count > MAX_PRUNE_COUNT)
                count = MAX_PRUNE_COUNT;

            var (opts, _) = OptionsParser.ParseFrom(new PruneOptions(), args);

            await RunPruneAsync(count,
                opts.Safe
                    ? static m => !m.IsPinned
                    : static _ => true,
                opts.After);
        }

        //prune @user [x]
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        [NadekoOptions<PruneOptions>]
        [Priority(0)]
        public Task Prune(IGuildUser user, int count = DEFAULT_PRUNE_COUNT, params string[] args)
            => Prune(user.Id, count, args);

        //prune userid [x]
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        [NadekoOptions<PruneOptions>]
        [Priority(0)]
        public async Task Prune(ulong userId, int count = DEFAULT_PRUNE_COUNT, params string[] args)
        {
            if (userId == ctx.User.Id)
                count++;

            if (count < 1)
                return;

            if (count > MAX_PRUNE_COUNT)
                count = MAX_PRUNE_COUNT;

            var (opts, _) = OptionsParser.ParseFrom(new PruneOptions(), args);

            await RunPruneAsync(count,
                opts.Safe
                    ? m => m.Author.Id == userId && !m.IsPinned
                    : m => m.Author.Id == userId,
                opts.After,
                DateTimeOffset.UtcNow - _twoWeeks);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(ChannelPerm.ManageMessages)]
        [BotPerm(ChannelPerm.ManageMessages)]
        public async Task PruneCancel()
        {
            if (!_service.Cancel(PruneService.GetPruneKey(ctx.Channel)))
            {
                await Response().Error(strs.prune_not_found).SendAsync();
                return;
            }

            await Response().Confirm(strs.prune_cancelled).SendAsync();
        }

        private async Task RunPruneAsync(
            int count,
            Func<IMessage, bool> predicate,
            ulong? after,
            DateTimeOffset? notOlderThan = null)
        {
            using var session = _service.TryStart(ctx.Channel);

            if (session is null)
            {
                var msg = await Response().Pending(strs.prune_already_running).SendAsync();
                msg.DeleteAfter(5);
                return;
            }

            var progressMsg = await Response().Pending(strs.prune_progress(0, count)).SendAsync();
            var cancelBtn = await AttachCancelButtonAsync(progressMsg, session);

            PruneResult result;
            await using (var pump = new PruneProgressPump(progressMsg, deleted => CreateEmbed()
                             .WithPendingColor()
                             .WithDescription(GetText(strs.prune_progress(deleted, count)))
                             .Build()))
            {
                result = await _service.PruneWhere(session,
                    ctx.Channel,
                    count,
                    m => m.Id != progressMsg.Id && predicate(m),
                    pump,
                    after,
                    notOlderThan);
            }

            cancelBtn?.SetCompleted();

            await SendResultAsync(result, progressMsg);
        }

        private async Task<NadekoInteractionBase> AttachCancelButtonAsync(
            IUserMessage progressMsg,
            PruneSession session)
        {
            var handler = _inter.Create(ctx.User.Id,
                new ButtonBuilder(
                    label: GetText(strs.prune_btn_cancel),
                    customId: "prune:cancel",
                    style: ButtonStyle.Danger),
                async smc =>
                {
                    session.Cancel();
                    await smc.DeferAsync();
                },
                clearAfter: false);

            var cb = new ComponentBuilder();
            handler.AddTo(cb);

            try
            {
                await progressMsg.ModifyAsync(m => m.Components = cb.Build());
            }
            catch (HttpException)
            {
                // the prune still runs without the cancel button, .prunecancel remains available
                return null;
            }

            _ = handler.RunAsync(progressMsg);
            return handler;
        }

        private async Task SendResultAsync(PruneResult result, IUserMessage progressMsg)
        {
            try
            {
                switch (result)
                {
                    case PruneResult.Success:
                        await progressMsg.DeleteAsync();
                        break;
                    case PruneResult.Cancelled:
                        await FinalizeProgressMsgAsync(progressMsg, false, strs.prune_cancelled);
                        break;
                    default:
                        await FinalizeProgressMsgAsync(progressMsg, true, strs.error_occured);
                        break;
                }
            }
            catch (HttpException)
            {
                // progress message is gone, nothing to update
            }
        }

        private async Task FinalizeProgressMsgAsync(IUserMessage progressMsg, bool isError, LocStr text)
        {
            var eb = CreateEmbed().WithDescription(GetText(text));
            eb = isError ? eb.WithErrorColor() : eb.WithPendingColor();

            await progressMsg.ModifyAsync(m =>
            {
                m.Embed = eb.Build();
                m.Components = new ComponentBuilder().Build();
            });

            progressMsg.DeleteAfter(5);
        }

        /// <summary>
        /// Edits the progress message on a fixed interval instead of once per reported batch.
        /// Reports are volatile writes, a single pump loop is the only writer to the message.
        /// </summary>
        private sealed class PruneProgressPump : IProgress<(int deleted, int total)>, IAsyncDisposable
        {
            private static readonly TimeSpan _editInterval = TimeSpan.FromSeconds(2);

            private readonly IUserMessage _msg;
            private readonly Func<int, Embed> _render;
            private readonly CancellationTokenSource _cts = new();
            private readonly Task _pumpTask;

            private int _deleted;

            public PruneProgressPump(IUserMessage msg, Func<int, Embed> render)
            {
                _msg = msg;
                _render = render;
                _pumpTask = Task.Run(PumpInternalAsync);
            }

            public void Report((int deleted, int total) value)
                => Volatile.Write(ref _deleted, value.deleted);

            private async Task PumpInternalAsync()
            {
                var opts = new RequestOptions
                {
                    CancelToken = _cts.Token
                };

                using var timer = new PeriodicTimer(_editInterval);
                var lastShown = 0;

                try
                {
                    while (await timer.WaitForNextTickAsync(_cts.Token))
                    {
                        var current = Volatile.Read(ref _deleted);
                        if (current == lastShown)
                            continue;

                        lastShown = current;

                        try
                        {
                            await _msg.ModifyAsync(m => m.Embed = _render(current), opts);
                        }
                        catch (HttpException)
                        {
                            // the progress message is gone, further edits would keep failing
                            return;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }

            public async ValueTask DisposeAsync()
            {
                await _cts.CancelAsync();

                try
                {
                    await _pumpTask;
                }
                catch (OperationCanceledException)
                {
                }

                _cts.Dispose();
            }
        }
    }
}
