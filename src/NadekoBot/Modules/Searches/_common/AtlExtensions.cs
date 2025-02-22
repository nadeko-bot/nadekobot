#nullable disable
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Searches;

public static class AtlExtensions
{
    public static Task<AutoTranslateChannel> GetByChannelId(this IQueryable<AutoTranslateChannel> set, ulong channelId)
        => set.Include(x => x.Users).FirstOrDefaultAsyncEF(x => x.ChannelId == channelId);
}