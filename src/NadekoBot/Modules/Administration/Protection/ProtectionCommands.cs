#nullable disable
using NadekoBot.Common.TypeReaders.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public partial class Administration
{
    [Group]
    public partial class ProtectionCommands : NadekoModule<ProtectionService>
    {
        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AntiAlt()
        {
            if (await _service.TryStopAntiAltAsync(ctx.Guild.Id))
            {
                await Response().Confirm(strs.prot_disable("Anti-Alt")).SendAsync();
                return;
            }

            await Response().Confirm(strs.protection_not_running("Anti-Alt")).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AntiAlt(
            ParsedTimespan minAge,
            PunishmentAction action,
            [Leftover] ParsedTimespan punishTimespan = null)
        {
            var minAgeMinutes = (int)minAge.Time.TotalMinutes;
            var punishTimeMinutes = (int?)punishTimespan?.Time.TotalMinutes ?? 0;

            if (minAgeMinutes < 1 || punishTimeMinutes < 0)
                return;

            var minutes = (int?)punishTimespan?.Time.TotalMinutes ?? 0;
            if (action is PunishmentAction.TimeOut && minutes < 1)
                minutes = 1;

            await _service.StartAntiAltAsync(ctx.Guild.Id,
                minAgeMinutes,
                action,
                minutes);

            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AntiAlt(ParsedTimespan minAge, PunishmentAction action, [Leftover] IRole role)
        {
            var minAgeMinutes = (int)minAge.Time.TotalMinutes;

            if (minAgeMinutes < 1)
                return;

            if (action == PunishmentAction.TimeOut)
                return;

            await _service.StartAntiAltAsync(ctx.Guild.Id, minAgeMinutes, action, roleId: role.Id);

            await ctx.OkAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AntiRaid()
        {
            if (await _service.TryStopAntiRaidAsync(ctx.Guild.Id))
            {
                await Response().Confirm(strs.prot_disable("Anti-Raid")).SendAsync();
                return;
            }

            await Response().Pending(strs.protection_not_running("Anti-Raid")).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(1)]
        public Task AntiRaid(
            int userThreshold,
            int seconds,
            PunishmentAction action,
            [Leftover] ParsedTimespan punishTimespan)
            => InternalAntiRaid(userThreshold, seconds, action, punishTimespan);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(2)]
        public Task AntiRaid(int userThreshold, int seconds, PunishmentAction action)
            => InternalAntiRaid(userThreshold, seconds, action);

        private async Task InternalAntiRaid(
            int userThreshold,
            int seconds = 10,
            PunishmentAction action = PunishmentAction.Mute,
            ParsedTimespan punishTimespan = null)
        {
            if (action == PunishmentAction.AddRole)
            {
                await Response().Error(strs.punishment_unsupported(action)).SendAsync();
                return;
            }

            if (userThreshold is < 2 or > 30)
            {
                await Response().Error(strs.raid_cnt(2, 30)).SendAsync();
                return;
            }

            if (seconds is < 2 or > 300)
            {
                await Response().Error(strs.raid_time(2, 300)).SendAsync();
                return;
            }

            if (punishTimespan is not null)
            {
                if (!_service.IsDurationAllowed(action))
                    await Response().Error(strs.prot_cant_use_time).SendAsync();
            }

            var time = (int?)punishTimespan?.Time.TotalMinutes ?? 0;
            if (time is < 0 or > 60 * 24)
                return;

            if (action is PunishmentAction.TimeOut && time < 1)
                return;

            var stats = await _service.StartAntiRaidAsync(ctx.Guild.Id, userThreshold, seconds, action, time);

            if (stats is null)
                return;

            await Response()
                  .Confirm(GetText(strs.prot_enable("Anti-Raid")),
                      $"{ctx.User.Mention} {GetAntiRaidString(stats)}")
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AntiSpam()
        {
            if (await _service.TryStopAntiSpamAsync(ctx.Guild.Id))
            {
                await Response().Confirm(strs.prot_disable("Anti-Spam")).SendAsync();
                return;
            }

            await Response().Pending(strs.protection_not_running("Anti-Spam")).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(0)]
        public Task AntiSpam(int messageCount, PunishmentAction action, [Leftover] IRole role)
        {
            if (action != PunishmentAction.AddRole)
                return Task.CompletedTask;

            return InternalAntiSpam(messageCount, action, null, role);
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(1)]
        public Task AntiSpam(int messageCount, PunishmentAction action, [Leftover] ParsedTimespan punishTimespan)
            => InternalAntiSpam(messageCount, action, punishTimespan);

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        [Priority(2)]
        public Task AntiSpam(int messageCount, PunishmentAction action)
            => InternalAntiSpam(messageCount, action);

        private async Task InternalAntiSpam(
            int messageCount,
            PunishmentAction action,
            ParsedTimespan timespanData = null,
            IRole role = null)
        {
            if (messageCount is < 2 or > 10)
                return;

            if (timespanData is not null)
            {
                if (!_service.IsDurationAllowed(action))
                    await Response().Error(strs.prot_cant_use_time).SendAsync();
            }

            var time = (int?)timespanData?.Time.TotalMinutes ?? 0;
            if (time is < 0 or > 60 * 24)
                return;

            if (action is PunishmentAction.TimeOut && time < 1)
                return;

            var stats = await _service.StartAntiSpamAsync(ctx.Guild.Id, messageCount, action, time, role?.Id);

            await Response()
                  .Confirm(GetText(strs.prot_enable("Anti-Spam")),
                      $"{ctx.User.Mention} {GetAntiSpamString(stats)}")
                  .SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.Administrator)]
        public async Task AntispamIgnore()
        {
            var added = await _service.AntiSpamIgnoreAsync(ctx.Guild.Id, ctx.Channel.Id);

            if (added is null)
            {
                await Response().Error(strs.protection_not_running("Anti-Spam")).SendAsync();
                return;
            }

            if (added.Value)
                await Response().Confirm(strs.spam_ignore("Anti-Spam")).SendAsync();
            else
                await Response().Confirm(strs.spam_not_ignore("Anti-Spam")).SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        public async Task AntiList()
        {
            var (spam, raid, alt) = _service.GetAntiStats(ctx.Guild.Id);

            if (spam is null && raid is null && alt is null)
            {
                await Response().Confirm(strs.prot_none).SendAsync();
                return;
            }

            var embed = CreateEmbed().WithOkColor().WithTitle(GetText(strs.prot_active));

            if (spam is not null)
                embed.AddField("Anti-Spam", GetAntiSpamString(spam).TrimTo(1024), true);

            if (raid is not null)
                embed.AddField("Anti-Raid", GetAntiRaidString(raid).TrimTo(1024), true);

            if (alt is not null)
                embed.AddField("Anti-Alt", GetAntiAltString(alt), true);

            await Response().Embed(embed).SendAsync();
        }

        private string GetAntiAltString(AntiAltStats alt)
            => GetText(strs.anti_alt_status(Format.Bold(alt.MinAge.ToString(@"dd\d\ hh\h\ mm\m\ ")),
                Format.Bold(alt.Action.ToString()),
                Format.Bold(alt.Counter.ToString())));

        private string GetAntiSpamString(AntiSpamStats stats)
        {
            var settings = stats.AntiSpamSettings;
            var ignoredString = string.Join(", ", settings.IgnoredChannels.Select(c => $"<#{c.ChannelId}>"));

            if (string.IsNullOrWhiteSpace(ignoredString))
                ignoredString = "none";

            var add = string.Empty;
            if (settings.MuteTime > 0)
                add = $" ({TimeSpan.FromMinutes(settings.MuteTime):hh\\hmm\\m})";

            return GetText(strs.spam_stats(Format.Bold(settings.MessageThreshold.ToString()),
                Format.Bold(settings.Action + add),
                ignoredString));
        }

        private string GetAntiRaidString(AntiRaidStats stats)
        {
            var actionString = Format.Bold(stats.AntiRaidSettings.Action.ToString());

            if (stats.AntiRaidSettings.PunishDuration > 0)
                actionString += $" **({TimeSpan.FromMinutes(stats.AntiRaidSettings.PunishDuration):hh\\hmm\\m})**";

            return GetText(strs.raid_stats(Format.Bold(stats.AntiRaidSettings.UserThreshold.ToString()),
                Format.Bold(stats.AntiRaidSettings.Seconds.ToString()),
                actionString));
        }
    }
}