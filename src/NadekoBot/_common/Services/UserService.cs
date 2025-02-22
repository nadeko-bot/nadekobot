using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp.Services;

public sealed class UserService : IUserService, INService
{
    private readonly DbService _db;

    public UserService(DbService db)
    {
        _db = db;
    }

    public async Task<DiscordUser?> GetUserAsync(ulong userId)
    {
        await using var uow = _db.GetDbContext();
        var user = await uow
                         .GetTable<DiscordUser>()
                         .FirstOrDefaultAsyncLinqToDB(u => u.UserId == userId);

        return user;
    }
}