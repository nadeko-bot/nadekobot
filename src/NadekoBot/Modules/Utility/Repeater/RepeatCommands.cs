using NadekoBot.Common.TypeReaders;
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Utility.Services;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class RepeatCommands : NadekoModule<RepeaterService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task RepeatSkip(int index)
        {
            if (--index < 0)
                return;

            var result = await _service.ToggleSkipNextAsync(ctx.Guild.Id, index);

            if (result is null)
            {
                await Response().Error(strs.index_out_of_range).SendAsync();
                return;
            }

            if (result is true)
            {
                await Response().Confirm(strs.repeater_skip_next).SendAsync();
            }
            else
            {
                await Response().Confirm(strs.repeater_dont_skip_next).SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task RepeatInvoke(int index)
        {
            if (--index < 0)
                return;

            var success = await _service.TriggerExternal(ctx.Guild.Id, index);
            if (!success)
                await Response().Error(strs.repeat_invoke_none).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task RepeatRemove(int index)
        {
            if (--index < 0)
                return;

            var removed = await _service.RemoveByIndexAsync(ctx.Guild.Id, index);
            if (removed is null)
            {
                await Response().Error(strs.repeater_remove_fail).SendAsync();
                return;
            }

            var description = GetRepeaterInfoString(removed);
            await Response().Embed(CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.repeater_removed(index + 1)))
                .WithDescription(description)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task RepeatRedundant(int index)
        {
            if (--index < 0)
                return;

            var result = await _service.ToggleRedundantAsync(ctx.Guild.Id, index);

            if (result is null)
            {
                await Response().Error(strs.index_out_of_range).SendAsync();
                return;
            }

            if (result.Value)
                await Response().Error(strs.repeater_redundant_no(index + 1)).SendAsync();
            else
                await Response().Confirm(strs.repeater_redundant_yes(index + 1)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(-2)]
        public Task Repeat([Leftover] string message)
            => Repeat(ctx.Channel, null, null, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(-1)]
        public Task Repeat(ITextChannel channel, [Leftover] string message)
            => Repeat(channel, null, null, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(0)]
        public Task Repeat(ParsedTimespan interval, [Leftover] string message)
            => Repeat(ctx.Channel, null, interval, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(0)]
        public Task Repeat(ITextChannel channel, ParsedTimespan interval, [Leftover] string message)
            => Repeat(channel, null, interval, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public Task Repeat(GuildDateTime timeOfDay, [Leftover] string message)
            => Repeat(timeOfDay, null, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(1)]
        public Task Repeat(ITextChannel channel, GuildDateTime timeOfDay, [Leftover] string message)
            => Repeat(channel, timeOfDay, null, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(2)]
        public Task Repeat(GuildDateTime? timeOfDay, ParsedTimespan? interval, [Leftover] string message)
            => Repeat(ctx.Channel, timeOfDay, interval, message);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        [Priority(3)]
        public async Task Repeat(IMessageChannel channel, GuildDateTime? timeOfDay, ParsedTimespan? interval,
            [Leftover] string message)
        {
            if (channel is not ITextChannel txtCh || txtCh.GuildId != ctx.Guild.Id)
                return;

            var perms = ((IGuildUser)ctx.User).GetPermissions(txtCh);
            if (!perms.SendMessages)
                return;

            var startTimeOfDay = timeOfDay?.InputTimeUtc.TimeOfDay;
            // if interval not null, that means user specified it (don't change it)

            // if interval is null set the default to:
            // if time of day is specified: 1 day
            // else 5 minutes
            var realInterval =
                interval?.Time ?? (startTimeOfDay is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromDays(1));

            if (string.IsNullOrWhiteSpace(message)
                || (interval is not null
                    && (interval.Time > TimeSpan.FromMinutes(25000) || interval.Time < TimeSpan.FromMinutes(1))))
                return;

            message = ((IGuildUser)ctx.User).GuildPermissions.MentionEveryone
                ? message
                : message.SanitizeMentions(true);

            var runner = await _service.AddRepeaterAsync(channel.Id,
                ctx.Guild.Id,
                realInterval,
                message,
                false,
                startTimeOfDay);

            if (runner is null)
            {
                await Response().Error(strs.repeater_exceed_limit(5)).SendAsync();
                return;
            }

            var description = GetRepeaterInfoString(runner);
            await Response().Embed(CreateEmbed()
                .WithOkColor()
                .WithTitle(GetText(strs.repeater_created))
                .WithDescription(description)).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageMessages)]
        public async Task RepeatList()
        {
            var repeaters = _service.GetRepeaters(ctx.Guild.Id);
            if (repeaters.Count == 0)
            {
                await Response().Confirm(strs.repeaters_none).SendAsync();
                return;
            }

            var embed = CreateEmbed().WithTitle(GetText(strs.list_of_repeaters)).WithOkColor();

            var i = 0;
            foreach (var runner in repeaters.OrderBy(r => r.Repeater.Id))
            {
                var description = GetRepeaterInfoString(runner);
                var name = $"#`{++i}` {(_service.IsRepeaterSkipped(runner.Repeater.Id) ? "🦘" : "")}";
                embed.AddField(name, description);
            }

            await Response().Embed(embed).SendAsync();
        }

        private string GetRepeaterInfoString(RunningRepeater runner)
        {
            var intervalString = Format.Bold(runner.Repeater.Interval.ToPrettyStringHm());
            var executesIn = runner.NextTime < DateTime.UtcNow ? TimeSpan.Zero : runner.NextTime - DateTime.UtcNow;
            var executesInString = Format.Bold(executesIn.ToPrettyStringHm());
            var message = Format.Sanitize(runner.Repeater.Message.TrimTo(50));

            var description = string.Empty;
            if (_service.IsNoRedundant(runner.Repeater.Id))
                description = Format.Underline(Format.Bold(GetText(strs.no_redundant))) + "\n\n";

            description += $"<#{runner.Repeater.ChannelId}>\n"
                           + $"`{GetText(strs.interval_colon)}` {intervalString}\n"
                           + $"`{GetText(strs.executes_in_colon)}` {executesInString}\n"
                           + $"`{GetText(strs.message_colon)}` {message}";

            return description;
        }
    }
}