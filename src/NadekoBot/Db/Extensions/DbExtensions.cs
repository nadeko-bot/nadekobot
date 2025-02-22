#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class DbExtensions
{
    public static T GetById<T>(this DbSet<T> set, int id)
        where T : DbEntity
        => set.FirstOrDefault(x => x.Id == id);

    public static GuildFilterConfig FilterConfigForId(
        this DbContext ctx,
        ulong guildId,
        Func<IQueryable<GuildFilterConfig>, IQueryable<GuildFilterConfig>> includes = default)
    {
        includes ??= static set => set;

        var gfc = includes(ctx.Set<GuildFilterConfig>()
                              .Where(gc => gc.GuildId == guildId))
            .FirstOrDefault();

        if (gfc is null)
        {
            ctx.Add(gfc = new()
            {
                GuildId = guildId,
            });
        }

        return gfc;
    }

    public static GuildConfig GuildConfigsForId(
        this DbContext ctx,
        ulong guildId,
        Func<IQueryable<GuildConfig>, IQueryable<GuildConfig>> includes = default)
    {
        includes ??= static set => set;

        var gc = includes(ctx.Set<GuildConfig>()
                             .Where(gc => gc.GuildId == guildId))
            .FirstOrDefault();

        if (gc is null)
        {
            ctx.Add(gc = new()
            {
                GuildId = guildId,
            });
        }

        return gc;
    }
}