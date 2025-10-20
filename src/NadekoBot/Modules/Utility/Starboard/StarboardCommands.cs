using Microsoft.EntityFrameworkCore;
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

    [Cmd("starboard channel")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardChannel(ITextChannel? ch = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>().FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting
                {
                    GuildId = this.ctx.Guild.Id,
                };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.StarboardChannelId = ch?.Id;
            await dbc.SaveChangesAsync();

            if (ch is null)
                await Response().Confirm("Starboard channel cleared.").SendAsync();
            else
                await Response().Confirm($"Starboard channel set to {ch.Mention}.").SendAsync();
        }

        [Cmd("starboard")]
        [RequireContext(ContextType.Guild)]
        public async Task StarboardStatus()
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>().FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            
            if (g is null)
            {
                await Response().Error("Starboard not configured for this server.").SendAsync();
                return;
            }

            var channel = g.StarboardChannelId.HasValue ? $"<#{g.StarboardChannelId}>" : "Not set";
            var status = g.IsEnabled ? "✅ Enabled" : "❌ Disabled";
            
            var eb = new EmbedBuilder()
                .WithTitle("Starboard Configuration")
                .AddField("Status", status, true)
                .AddField("Channel", channel, true)
                .AddField("Threshold", g.Threshold.ToString(), true)
                .AddField("Emoji", g.Emoji, true)
                .AddField("Self-star", g.AllowSelfStar ? "Allowed" : "Disallowed", true)
                .AddField("Bot messages", g.AllowBotMessages ? "Allowed" : "Disallowed", true)
                .AddField("Strict emoji", g.StrictEmoji ? "Enabled" : "Disabled", true);
                
            await Response().Embed(eb).SendAsync();
        }

    [Cmd("starboard enable")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardEnable(bool? enabled = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting { GuildId = this.ctx.Guild.Id };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.IsEnabled = enabled ?? !g.IsEnabled;
            await dbc.SaveChangesAsync();

            await Response().Confirm($"Starboard {(g.IsEnabled ? "enabled" : "disabled")}.").SendAsync();
        }

    [Cmd("starboard threshold")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardThreshold(int threshold)
        {
            if (threshold < 1 || threshold > 100)
                return;

            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting { GuildId = this.ctx.Guild.Id };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.Threshold = threshold;
            await dbc.SaveChangesAsync();
            await Response().Confirm($"Starboard threshold set to {threshold}.").SendAsync();
        }

    [Cmd("starboard emoji")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardEmoji([Leftover] string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return;

            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting { GuildId = this.ctx.Guild.Id };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.Emoji = emoji.Trim();
            await dbc.SaveChangesAsync();

            await Response().Confirm($"Starboard emoji set to {emoji}.").SendAsync();
        }

    [Cmd("starboard self")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardSelf(bool? allow = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting { GuildId = this.ctx.Guild.Id };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.AllowSelfStar = allow ?? !g.AllowSelfStar;
            await dbc.SaveChangesAsync();
            await Response().Confirm($"Self-star {(g.AllowSelfStar ? "allowed" : "disallowed")}.").SendAsync();
        }

    [Cmd("starboard bots")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardBots(bool? allow = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting { GuildId = this.ctx.Guild.Id };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.AllowBotMessages = allow ?? !g.AllowBotMessages;
            await dbc.SaveChangesAsync();
            await Response().Confirm($"Bot messages {(g.AllowBotMessages ? "allowed" : "disallowed")}.").SendAsync();
        }

    [Cmd("starboard strict")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardStrict(bool? strict = null)
        {
            await using var dbc = _db.GetDbContext();
            var g = await dbc.Set<StarboardSetting>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id);
            if (g is null)
            {
                g = new StarboardSetting { GuildId = this.ctx.Guild.Id };
                dbc.Set<StarboardSetting>().Add(g);
            }

            g.StrictEmoji = strict ?? !g.StrictEmoji;
            await dbc.SaveChangesAsync();
            await Response().Confirm($"Strict emoji {(g.StrictEmoji ? "enabled" : "disabled")}.").SendAsync();
        }

    [Cmd("starboard ignore")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardIgnore(ITextChannel ch)
        {
            await using var dbc = _db.GetDbContext();
            var exists = await dbc.Set<StarboardIgnoredChannel>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id && x.ChannelId == ch.Id);

            if (exists is null)
            {
                dbc.Set<StarboardIgnoredChannel>().Add(new StarboardIgnoredChannel
                {
                    GuildId = this.ctx.Guild.Id,
                    ChannelId = ch.Id,
                });
                await dbc.SaveChangesAsync();
                await Response().Confirm($"Channel {ch.Mention} ignored.").SendAsync();
            }
            else
            {
                dbc.Set<StarboardIgnoredChannel>().Remove(exists);
                await dbc.SaveChangesAsync();
                await Response().Confirm($"Channel {ch.Mention} unignored.").SendAsync();
            }
        }

    [Cmd("starboard channelthreshold")]
        [RequireContext(ContextType.Guild)]
        [UserPerm(GuildPerm.ManageGuild)]
        public async Task StarboardChannelThreshold(ITextChannel ch, int? threshold = null)
        {
            await using var dbc = _db.GetDbContext();

            var ov = await dbc.Set<StarboardChannelOverride>()
                .FirstOrDefaultAsync(x => x.GuildId == this.ctx.Guild.Id && x.ChannelId == ch.Id);

            if (threshold is null)
            {
                if (ov is not null)
                {
                    dbc.Set<StarboardChannelOverride>().Remove(ov);
                    await dbc.SaveChangesAsync();
                }

                await Response().Confirm($"Override for {ch.Mention} cleared.").SendAsync();
                return;
            }

            if (ov is null)
            {
                dbc.Set<StarboardChannelOverride>().Add(new StarboardChannelOverride
                {
                    GuildId = this.ctx.Guild.Id,
                    ChannelId = ch.Id,
                    Threshold = threshold
                });
            }
            else
            {
                ov.Threshold = threshold;
            }
            await dbc.SaveChangesAsync();

            await Response().Confirm($"Override for {ch.Mention} set to {threshold}.").SendAsync();
        }
    }
}
