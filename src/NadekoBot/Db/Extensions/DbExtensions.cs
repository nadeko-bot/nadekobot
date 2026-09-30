#nullable disable
using LinqToDB.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
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

    private const int SQLITE_CONSTRAINT_UNIQUE = 2067;

    // the columns have no sql defaults, so the missing row is inserted through ef with the model defaults
    public static async Task EnsureGuildConfigAsync(this DbContext ctx, ulong guildId)
    {
        if (await ctx.GetTable<GuildConfig>().AnyAsyncLinqToDB(x => x.GuildId == guildId))
            return;

        var gc = new GuildConfig { GuildId = guildId };
        ctx.Add(gc);
        try
        {
            await ctx.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
                                           {
                                               SqliteExtendedErrorCode: SQLITE_CONSTRAINT_UNIQUE
                                           })
        {
            // another command created the row at the same time
        }
        finally
        {
            ctx.Entry(gc).State = EntityState.Detached;
        }
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