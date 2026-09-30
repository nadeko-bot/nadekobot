using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace NadekoBot.Modules.Utility.LineUp;

public sealed class LineUpService(DbService db) : INService
{
    private const int SQLITE_CONSTRAINT_PRIMARYKEY = 1555;

    public async Task<(bool Success, int Position)> TryJoinLineupAsync(
        ulong guildId,
        ulong channelId,
        ulong userId,
        string? reason)
    {
        await using var ctx = db.GetDbContext();

        var dateAdded = DateTime.UtcNow;
        try
        {
            await ctx.GetTable<LineUpUser>()
                .InsertAsync(() => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    UserId = userId,
                    Reason = reason,
                    DateAdded = dateAdded
                });
        }
        // the primary key is the only atomic "already joined" check, because linq2db runs a no-update upsert as SELECT then INSERT
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == SQLITE_CONSTRAINT_PRIMARYKEY)
        {
            return (false, 0);
        }

        var position = await GetPositionAsync(guildId, channelId, userId);
        return (true, position ?? 0);
    }

    public async Task<bool> TryLeaveLineupAsync(ulong guildId, ulong channelId, ulong userId)
    {
        await using var ctx = db.GetDbContext();

        var rowsAffected = await ctx.GetTable<LineUpUser>()
            .Where(lu => lu.GuildId == guildId && lu.ChannelId == channelId && lu.UserId == userId)
            .DeleteAsync();

        return rowsAffected > 0;
    }

    public async Task<LineUpUser?> GetNextInLineupAsync(ulong guildId, ulong channelId)
    {
        await using var ctx = db.GetDbContext();
        var table = ctx.GetTable<LineUpUser>();

        // one statement, so two moderators calling next at once can't both get the same member
        var removed = await table
            .Where(lu => lu.GuildId == guildId
                         && lu.ChannelId == channelId
                         && lu.UserId
                         == table
                             .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                             .OrderBy(x => x.DateAdded)
                             .ThenBy(x => x.UserId)
                             .Select(x => x.UserId)
                             .FirstOrDefault())
            .DeleteWithOutputAsync();

        return removed.Length == 0 ? null : removed[0];
    }

    public async Task<int> GetLineupCountAsync(ulong guildId, ulong channelId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<LineUpUser>()
            .CountAsyncLinqToDB(lu => lu.GuildId == guildId && lu.ChannelId == channelId);
    }

    public async Task<IReadOnlyCollection<LineUpUser>> GetLineupPageAsync(
        ulong guildId,
        ulong channelId,
        int page,
        int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);

        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<LineUpUser>()
            .Where(lu => lu.GuildId == guildId && lu.ChannelId == channelId)
            .OrderBy(lu => lu.DateAdded)
            .ThenBy(lu => lu.UserId)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToArrayAsyncLinqToDB();
    }

    public async Task<int?> GetPositionAsync(ulong guildId, ulong channelId, ulong userId)
    {
        await using var ctx = db.GetDbContext();
        var table = ctx.GetTable<LineUpUser>();

        // linq2db compares sqlite dates through DateTime(), which drops the fraction of a second, so rank by ORDER BY instead
        var position = await table
            .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
            .Select(x => new
            {
                x.UserId,
                Position = Sql.Ext.RowNumber().Over().OrderBy(x.DateAdded).ThenBy(x.UserId).ToValue()
            })
            .AsSubQuery()
            .Where(x => x.UserId == userId)
            .Select(x => (long?)x.Position)
            .FirstOrDefaultAsyncLinqToDB();

        return (int?)position;
    }

    public async Task<int> ClearLineupAsync(ulong guildId, ulong channelId)
    {
        await using var ctx = db.GetDbContext();

        return await ctx.GetTable<LineUpUser>()
            .Where(lu => lu.GuildId == guildId && lu.ChannelId == channelId)
            .DeleteAsync();
    }
}
