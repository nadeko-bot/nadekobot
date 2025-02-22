using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public class TempRoleService : IReadyExecutor, INService
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;

    private TaskCompletionSource<bool> _tcs = new();

    public TempRoleService(
        DbService db,
        DiscordSocketClient client,
        IBotCreds creds)
    {
        _db = db;
        _client = client;
        _creds = creds;
    }

    public async Task AddTempRoleAsync(
        ulong guildId,
        ulong roleId,
        ulong userId,
        TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            await using var uow = _db.GetDbContext();
            await uow.GetTable<TempRole>()
                     .Where(x => x.GuildId == guildId && x.UserId == userId)
                     .DeleteAsync();
            return;
        }

        var until = DateTime.UtcNow.Add(duration);
        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<TempRole>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         RoleId = roleId,
                         UserId = userId,
                         Remove = false,
                         ExpiresAt = until
                     },
                     (old) => new()
                     {
                         ExpiresAt = until,
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         UserId = userId,
                         RoleId = roleId
                     });

        _tcs.TrySetResult(true);
    }

    public async Task OnReadyAsync()
    {
        while (true)
        {
            try
            {
                _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var latest = await _db.GetDbContext()
                                      .GetTable<TempRole>()
                                      .Where(x => Queries.GuildOnShard(x.GuildId,
                                          _creds.TotalShards,
                                          _client.ShardId))
                                      .OrderBy(x => x.ExpiresAt)
                                      .FirstOrDefaultAsyncLinqToDB();

                if (latest == default)
                {
                    await _tcs.Task;
                    continue;
                }

                var now = DateTime.UtcNow;
                if (latest.ExpiresAt > now)
                {
                    await Task.WhenAny(Task.Delay(latest.ExpiresAt - now), _tcs.Task);
                    continue;
                }

                var deleted = await _db.GetDbContext()
                                       .GetTable<TempRole>()
                                       .Where(x => Queries.GuildOnShard(x.GuildId,
                                                       _creds.TotalShards,
                                                       _client.ShardId)
                                                   && x.ExpiresAt <= now)
                                       .DeleteWithOutputAsync();

                foreach (var d in deleted)
                {
                    try
                    {
                        await RemoveRole(d);
                    }
                    catch
                    {
                        Log.Warning("Unable to remove temp role {RoleId} from user {UserId}",
                            d.RoleId,
                            d.UserId);
                    }

                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error occurred in temprole loop");
                await Task.Delay(1000);
            }
        }
    }

    private async Task RemoveRole(TempRole tempRole)
    {
        var guild = _client.GetGuild(tempRole.GuildId);

        var role = guild?.GetRole(tempRole.RoleId);
        if (role is null)
            return;

        var user = guild?.GetUser(tempRole.UserId);
        if (user is null)
            return;

        await user.RemoveRoleAsync(role);
    }
}