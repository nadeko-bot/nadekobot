using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public sealed class TempRoleService(
    DbService db,
    DiscordSocketClient client,
    ShardData shardData,
    TimeProvider? timeProvider = null) : IReadyExecutor, INService
{
    private static readonly TimeSpan _removeGap = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _loopRetryDelay = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private volatile TaskCompletionSource<bool> _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task AddTempRoleAsync(
        ulong guildId,
        ulong roleId,
        ulong userId,
        TimeSpan duration)
    {
        if (duration == TimeSpan.Zero)
        {
            await using var uow = db.GetDbContext();
            await uow.GetTable<TempRole>()
                     .Where(x => x.GuildId == guildId && x.UserId == userId)
                     .DeleteAsync();
            return;
        }

        var until = _time.GetUtcNow().UtcDateTime.Add(duration);
        await using var ctx = db.GetDbContext();
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

        _wake.TrySetResult(true);
    }

    public Task OnReadyAsync()
    {
        _ = Task.Run(ExpiryLoopInternalAsync);
        return Task.CompletedTask;
    }

    private async Task ExpiryLoopInternalAsync()
    {
        while (true)
        {
            try
            {
                var wake = (_wake = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

                DateTime? nextAt;
                await using (var ctx = db.GetDbContext())
                {
                    nextAt = await ctx.GetTable<TempRole>()
                                      .Where(Queries.GuildOnShard<TempRole>(x => x.GuildId,
                                          shardData.TotalShards,
                                          shardData.ShardId))
                                      .OrderBy(x => x.ExpiresAt)
                                      .Select(x => (DateTime?)x.ExpiresAt)
                                      .FirstOrDefaultAsyncLinqToDB();
                }

                if (nextAt is null)
                {
                    await wake;
                    continue;
                }

                var now = _time.GetUtcNow().UtcDateTime;
                if (nextAt.Value > now)
                {
                    await Task.WhenAny(Task.Delay(TimedAction.GetWait(nextAt.Value, now), _time), wake);
                    continue;
                }

                TempRole[] expired;
                await using (var ctx = db.GetDbContext())
                {
                    expired = await ctx.GetTable<TempRole>()
                                       .Where(Queries.GuildOnShard<TempRole>(x => x.GuildId,
                                           shardData.TotalShards,
                                           shardData.ShardId))
                                       .Where(x => x.ExpiresAt <= now)
                                       .ToArrayAsyncLinqToDB();
                }

                foreach (var tempRole in expired)
                {
                    var retry = await TryRemoveRoleInternalAsync(tempRole);
                    await FinishInternalAsync(tempRole.Id, now, retry);
                    await Task.Delay(_removeGap, _time);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error occurred in temprole loop");
                await Task.Delay(_loopRetryDelay, _time);
            }
        }
    }

    // The row goes only after the role is gone, so a failed call cannot leave the role on the member for good.
    private async Task FinishInternalAsync(int id, DateTime cutoff, bool retry)
    {
        await using var ctx = db.GetDbContext();

        // ExpiresAt <= cutoff keeps a temp role which was extended while the bot removed it.
        var row = ctx.GetTable<TempRole>().Where(x => x.Id == id && x.ExpiresAt <= cutoff);
        if (!retry)
        {
            await row.DeleteAsync();
            return;
        }

        var retryAt = _time.GetUtcNow().UtcDateTime + TimedAction.RetryDelay;
        await row.Set(x => x.ExpiresAt, retryAt).UpdateAsync();
    }

    // Returns true when the removal can work later.
    private async Task<bool> TryRemoveRoleInternalAsync(TempRole tempRole)
    {
        var guild = client.GetGuild(tempRole.GuildId);
        if (guild is null)
            return false;

        if (!guild.IsConnected)
            return true;

        var role = guild.GetRole(tempRole.RoleId);
        if (role is null)
            return false;

        try
        {
            // The bot does not download all members, so a member who is not cached comes from REST.
            IGuildUser? user = guild.GetUser(tempRole.UserId);
            user ??= await client.Rest.GetGuildUserAsync(tempRole.GuildId, tempRole.UserId);

            if (user is null || !user.RoleIds.Contains(role.Id))
                return false;

            await user.RemoveRoleAsync(role);
            return false;
        }
        catch (Exception ex)
        {
            var retry = TimedAction.ShouldRetry(ex);
            Log.Warning("Unable to remove temp role {RoleId} from user {UserId} in {GuildId} (retry: {Retry}): {ErrorMessage}",
                tempRole.RoleId,
                tempRole.UserId,
                tempRole.GuildId,
                retry,
                ex.Message);
            return retry;
        }
    }
}
