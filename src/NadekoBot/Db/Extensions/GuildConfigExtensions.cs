#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class GuildConfigExtensions
{
    private static List<WarningPunishment> DefaultWarnPunishments
        =>
        [
            new()
            {
                Count = 3,
                Punishment = PunishmentAction.Kick
            },

            new()
            {
                Count = 5,
                Punishment = PunishmentAction.Ban
            }
        ];

    /// <summary>
    ///     Gets full stream role settings for the guild with the specified id.
    /// </summary>
    /// <param name="ctx">Db Context</param>
    /// <param name="guildId">Id of the guild to get stream role settings for.</param>
    /// <returns>Guild'p stream role settings</returns>
    public static async Task<StreamRoleSettings> GetOrCreateStreamRoleSettings(this DbContext ctx, ulong guildId)
    {
        var srs = await ctx.GetTable<StreamRoleSettings>()
            .Where(x => x.GuildId == guildId)
            .FirstOrDefaultAsyncEF();

        if (srs is not null)
            return srs;

        srs = new()
        {
            GuildId = guildId,
        };

        ctx.Set<StreamRoleSettings>().Add(srs);

        return srs;
    }

    public static LogSetting LogSettingsFor(this DbContext ctx, ulong guildId)
    {
        var logSetting = ctx.Set<LogSetting>()
            .AsQueryable()
            .Include(x => x.LogIgnores)
            .Where(x => x.GuildId == guildId)
            .FirstOrDefault();

        if (logSetting is null)
        {
            ctx.Set<LogSetting>()
                .Add(logSetting = new()
                {
                    GuildId = guildId
                });
            ctx.SaveChanges();
        }

        return logSetting;
    }


    public static IEnumerable<GuildConfig> PermissionsForAll(this DbSet<GuildConfig> configs, List<ulong> include)
    {
        var query = configs.AsQueryable().Where(x => include.Contains(x.GuildId)).Include(gc => gc.Permissions);

        return query.ToList();
    }

    public static GuildConfig GcWithPermissionsFor(this DbContext ctx, ulong guildId)
    {
        var config = ctx.Set<GuildConfig>()
            .AsQueryable()
            .Where(gc => gc.GuildId == guildId)
            .Include(gc => gc.Permissions)
            .FirstOrDefault();

        if (config is null) // if there is no guildconfig, create new one
        {
            ctx.Set<GuildConfig>()
                .Add(config = new()
                {
                    GuildId = guildId,
                    Permissions = Permissionv2.GetDefaultPermlist
                });
            ctx.SaveChanges();
        }
        else if (config.Permissions is null || !config.Permissions.Any()) // if no perms, add default ones
        {
            config.Permissions = Permissionv2.GetDefaultPermlist;
            ctx.SaveChanges();
        }

        return config;
    }

    public static async Task<XpSettings> XpSettingsFor(this DbContext ctx, ulong guildId,
        Func<IQueryable<XpSettings>, IQueryable<XpSettings>> includes = default)
    {
        includes ??= static set => set;

        var srs = await includes(ctx.GetTable<XpSettings>()
                .Where(x => x.GuildId == guildId))
            .FirstOrDefaultAsyncLinqToDB();

        if (srs is not null)
            return srs;

        srs = await ctx.GetTable<XpSettings>()
            .InsertWithOutputAsync(() => new()
            {
                GuildId = guildId,
                ServerExcluded = false,
            });

        return srs;
    }

    public class GeneratingChannel
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
    }
}