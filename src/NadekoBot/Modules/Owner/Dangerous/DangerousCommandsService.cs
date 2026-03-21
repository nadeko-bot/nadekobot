#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Waifus.Waifu.Db;

namespace NadekoBot.Modules.Owner.Services;

public class DangerousCommandsService : INService
{
    private readonly DbService _db;

    public DangerousCommandsService(DbService db)
        => _db = db;

    public async Task<int> ExecuteSql(string sql)
    {
        int res;
        await using var uow = _db.GetDbContext();
        res = await uow.Database.ExecuteSqlRawAsync(sql);
        return res;
    }

    public SelectResult SelectSql(string sql)
    {
        var result = new SelectResult
        {
            ColumnNames = new(),
            Results = new()
        };

        using var uow = _db.GetDbContext();
        var conn = uow.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        if (reader.HasRows)
        {
            for (var i = 0; i < reader.FieldCount; i++)
                result.ColumnNames.Add(reader.GetName(i));
            while (reader.Read())
            {
                var obj = new object[reader.FieldCount];
                reader.GetValues(obj);
                result.Results.Add(obj.Select(x => x.ToString()).ToArray());
            }
        }

        return result;
    }

    public async Task PurgeUserAsync(ulong userId)
    {
        await using var uow = _db.GetDbContext();

        // Remove fans of this waifu
        await uow.GetTable<WaifuFan>()
            .Where(x => x.WaifuUserId == userId)
            .DeleteAsync();

        // Remove this user's backing
        await uow.GetTable<WaifuFan>()
            .Where(x => x.UserId == userId)
            .DeleteAsync();

        // Remove cycle snapshots
        await uow.GetTable<WaifuCycleSnapshot>()
            .Where(x => x.WaifuUserId == userId)
            .DeleteAsync();

        // Remove cycles
        await uow.GetTable<WaifuCycle>()
            .Where(x => x.WaifuUserId == userId)
            .DeleteAsync();

        // Remove waifu
        await uow.GetTable<WaifuInfo>()
            .Where(x => x.UserId == userId)
            .DeleteAsync();

        // delete guild xp
        await uow.GetTable<UserXpStats>().DeleteAsync(x => x.UserId == userId);

        // delete currency transactions
        await uow.GetTable<CurrencyTransaction>().DeleteAsync(x => x.UserId == userId);

        // delete user, currency, and clubs go away with it
        await uow.GetTable<DiscordUser>().DeleteAsync(u => u.UserId == userId);
    }

    public class SelectResult
    {
        public List<string> ColumnNames { get; set; }
        public List<string[]> Results { get; set; }
    }
}