using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public partial class Utility
{
    [Group]
    public partial class StarboardCommands : NadekoModule
    {
        private readonly DbService _db;

        public StarboardCommands(DbService db)
        {
            _db = db;
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardChannel(ITextChannel? ch = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = dbc.Set<StarboardSetting>().FirstOrDefault(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = await dbc.Set<StarboardSetting>().InsertWithOutputAsync(() => new StarboardSetting
                {
                    GuildId = this.ctx.Guild.Id,
                });
            }

            g.StarboardChannelId = ch?.Id;
            await dbc.UpdateAsync(g);

            if (ch is null)
                await Response().Confirm("Starboard channel cleared.").SendAsync();
            else
                await Response().Confirm($"Starboard channel set to {ch.Mention}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardEnable(bool? enabled = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id) ??
                await dbc.Set<StarboardSetting>()
                    .InsertWithOutputAsync(() => new StarboardSetting { GuildId = this.ctx.Guild.Id });

            g.IsEnabled = enabled ?? !g.IsEnabled;
            await dbc.UpdateAsync(g);

            await Response().Confirm($"Starboard {(g.IsEnabled ? "enabled" : "disabled")}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardThreshold(int threshold)
        {
            if (threshold < 1 || threshold > 100)
                return;

            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id) ??
                await dbc.Set<StarboardSetting>()
                    .InsertWithOutputAsync(() => new StarboardSetting { GuildId = this.ctx.Guild.Id });

            g.Threshold = threshold;
            await dbc.UpdateAsync(g);
            await Response().Confirm($"Starboard threshold set to {threshold}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardEmoji([Leftover] string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return;

            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id) ??
                await dbc.Set<StarboardSetting>()
                    .InsertWithOutputAsync(() => new StarboardSetting { GuildId = this.ctx.Guild.Id });

            g.Emoji = emoji.Trim();
            await dbc.UpdateAsync(g);

            await Response().Confirm($"Starboard emoji set to {emoji}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardSelf(bool? allow = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id) ??
                await dbc.Set<StarboardSetting>()
                    .InsertWithOutputAsync(() => new StarboardSetting { GuildId = this.ctx.Guild.Id });

            g.AllowSelfStar = allow ?? !g.AllowSelfStar;
            await dbc.UpdateAsync(g);
            await Response().Confirm($"Self-star {(g.AllowSelfStar ? "allowed" : "disallowed")}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardBots(bool? allow = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id) ??
                await dbc.Set<StarboardSetting>()
                    .InsertWithOutputAsync(() => new StarboardSetting { GuildId = this.ctx.Guild.Id });

            g.AllowBotMessages = allow ?? !g.AllowBotMessages;
            await dbc.UpdateAsync(g);
            await Response().Confirm($"Bot messages {(g.AllowBotMessages ? "allowed" : "disallowed")}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardStrict(bool? strict = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id) ??
                await dbc.Set<StarboardSetting>()
                    .InsertWithOutputAsync(() => new StarboardSetting { GuildId = this.ctx.Guild.Id });

            g.StrictEmoji = strict ?? !g.StrictEmoji;
            await dbc.UpdateAsync(g);
            await Response().Confirm($"Strict emoji {(g.StrictEmoji ? "enabled" : "disabled")}.").SendAsync();
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardIgnore(ITextChannel ch)
        {
            await using var dbc = _db.GetDbContext();
            var exists = await dbc.Set<StarboardIgnoredChannel>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id && x.ChannelId == ch.Id);

            if (exists is null)
            {
                await dbc.Set<StarboardIgnoredChannel>().InsertAsync(() => new StarboardIgnoredChannel
                {
                    GuildId = this.ctx.Guild.Id,
                    ChannelId = ch.Id,
                });
                await Response().Confirm($"Channel {ch.Mention} ignored.").SendAsync();
            }
            else
            {
                await dbc.DeleteAsync(exists);
                await Response().Confirm($"Channel {ch.Mention} unignored.").SendAsync();
            }
        }

        [Cmd]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardChannelThreshold(ITextChannel ch, int? threshold = null)
        {
            await using var dbc = _db.GetDbContext();

            var ov = await dbc.Set<StarboardChannelOverride>()
                .FirstOrDefaultAsyncLinqToDB(x => x.GuildId == this.ctx.Guild.Id && x.ChannelId == ch.Id);

            if (threshold is null)
            {
                if (ov is not null)
                    await dbc.DeleteAsync(ov);

                await Response().Confirm($"Override for {ch.Mention} cleared.").SendAsync();
                return;
            }

            if (ov is null)
            {
                await dbc.Set<StarboardChannelOverride>().InsertAsync(() => new StarboardChannelOverride
                {
                    GuildId = this.ctx.Guild.Id,
                    ChannelId = ch.Id,
                    Threshold = threshold
                });
            }
            else
            {
                ov.Threshold = threshold;
                await dbc.UpdateAsync(ov);
            }

            await Response().Confirm($"Override for {ch.Mention} set to {threshold}.").SendAsync();
        }
    }
}
