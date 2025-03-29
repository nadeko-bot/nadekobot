using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp.Services;

public sealed class UserService(DbService db, DiscordSocketClient client) : IUserService, INService
{

    public async Task<DiscordUser?> GetUserAsync(ulong userId)
    {
        await using var uow = db.GetDbContext();
        var user = await uow
                         .GetTable<DiscordUser>()
                         .FirstOrDefaultAsyncLinqToDB(u => u.UserId == userId);

        return user;
    }

    public async Task<IReadOnlyDictionary<ulong, IUserData>> GetUsersAsync(IReadOnlyCollection<ulong> userIds)
    {
        var result = new Dictionary<ulong, IUserData>();
        
        var cachedUsers = userIds
            .Select(userId => (userId, user: client.GetUser(userId)))
            .Where(x => x.user is not null)
            .ToDictionary(x => x.userId, x => (IUserData)new UserData(
                x.user.Id,
                x.user.Username,
                x.user.GetAvatarUrl() ?? x.user.GetDefaultAvatarUrl()));
        
        foreach (var (userId, userData) in cachedUsers)
            result[userId] = userData;
        
        var remainingIds = userIds.Except(cachedUsers.Keys).ToList();
        if (remainingIds.Count == 0)
            return result;
        
        // Try to get remaining users from database
        await using var uow = db.GetDbContext();
        var dbUsers = await uow
            .GetTable<DiscordUser>()
            .Where(u => remainingIds.Contains(u.UserId))
            .ToListAsyncLinqToDB();
            
        foreach (var dbUser in dbUsers)
        {
            result[dbUser.UserId] = new UserData(
                dbUser.UserId,
                dbUser.Username,
                dbUser.AvatarId);
            remainingIds.Remove(dbUser.UserId);
        }

        return result;
    }

}

public interface IUserData
{
    ulong Id { get; }
    string? Username { get; }
    string? AvatarUrl { get; }
}

public record struct UserData(ulong Id, string? Username, string? AvatarUrl) : IUserData
{
    public override string ToString()
        => Username ?? Id.ToString();
}