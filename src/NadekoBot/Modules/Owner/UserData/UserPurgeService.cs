using System.Text;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.UserData;

namespace NadekoBot.Modules.Owner.UserData;

public sealed class UserPurgeService(DbService db) : INService
{
    public const int BATCH_SIZE = 500;

    private static readonly (string Select, string Write)[] _plans = BuildPlansInternal();

    // Returns the number of changed or deleted rows.
    public async Task<int> PurgeAsync(ulong userId)
    {
        var total = 0;
        foreach (var (select, write) in _plans)
            total += await RunPlanInternalAsync(select, write, (long)userId);

        return total;
    }

    // Some user columns have no index. The rowids are read first, because a read does not block the
    // other shards, and each write then changes one short batch by rowid.
    private async Task<int> RunPlanInternalAsync(string select, string write, long userId)
    {
        await using var ctx = db.GetDbContext();
        await using var lctx = ctx.CreateLinqToDBConnection();

        var user = new DataParameter("u", userId, DataType.Int64);
        var changed = 0;
        var last = 0L;
        while (true)
        {
            var ids = await lctx.QueryToListAsync<long>(select, user, new DataParameter("last", last, DataType.Int64));
            if (ids.Count == 0)
                return changed;

            last = ids[^1];
            changed += await lctx.ExecuteAsync(write, user, new DataParameter("ids", ToJsonInternal(ids), DataType.NVarChar));

            if (ids.Count < BATCH_SIZE)
                return changed;
        }
    }

    private static string ToJsonInternal(List<long> ids)
    {
        var sb = new StringBuilder((ids.Count * 20) + 2);
        sb.Append('[');
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(ids[i]);
        }

        return sb.Append(']').ToString();
    }

    private static (string Select, string Write)[] BuildPlansInternal()
    {
        var plans = new (string, string)[UserDataColumns.Changes.Length];
        for (var i = 0; i < plans.Length; i++)
        {
            var change = UserDataColumns.Changes[i];
            var where = $"\"{change.UserColumn}\" = @u"
                        + (change.ExtraWhere is { } extra ? $" AND ({extra})" : string.Empty);

            var select = $"SELECT rowid FROM \"{change.Table}\" WHERE {where} AND rowid > @last ORDER BY rowid LIMIT {BATCH_SIZE}";

            // The user filter stays on the write, so a row which changed owner since the read is left alone.
            var target = $"WHERE rowid IN (SELECT value FROM json_each(@ids)) AND {where}";
            string write;
            if (change.Set is not { } set)
            {
                write = $"DELETE FROM \"{change.Table}\" {target}";
            }
            else
            {
                var sb = new StringBuilder($"UPDATE \"{change.Table}\" SET ");
                for (var j = 0; j < set.Length; j++)
                {
                    if (j > 0)
                        sb.Append(", ");

                    sb.Append('"').Append(set[j].Column).Append("\" = ");
                    sb.Append(set[j].Value is { } value ? $"'{value.Replace("'", "''")}'" : "NULL");
                }

                write = sb.Append(' ').Append(target).ToString();
            }

            plans[i] = (select, write);
        }

        return plans;
    }
}
