using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public enum ChannelLockResult
{
    Success,
    AlreadyLocked,
    NotLocked
}

public sealed class ChannelLockService(DbService db) : INService
{
    public const ulong LOCK_MASK = (ulong)(ChannelPermission.SendMessages
                                           | ChannelPermission.SendMessagesInThreads
                                           | ChannelPermission.CreatePublicThreads
                                           | ChannelPermission.CreatePrivateThreads);

    // Manage Roles is the channel's Manage Permissions, which the bot needs to edit the overwrite
    public const ulong BOT_CHANNEL_MASK = (ulong)(ChannelPermission.ViewChannel | ChannelPermission.ManageRoles);

    public static bool IsLocked(ITextChannel channel)
    {
        var current = channel.GetPermissionOverwrite(channel.Guild.EveryoneRole);
        return current is { } ow && (ow.DenyValue & LOCK_MASK) == LOCK_MASK;
    }

    public async Task<ChannelLockResult> LockAsync(ITextChannel channel)
    {
        var everyone = channel.Guild.EveryoneRole;
        var current = channel.GetPermissionOverwrite(everyone) ?? OverwritePermissions.InheritAll;

        if ((current.DenyValue & LOCK_MASK) == LOCK_MASK)
            return ChannelLockResult.AlreadyLocked;

        await using var uow = db.GetDbContext();
        var table = uow.GetTable<LockedChannel>();
        var saved = await table.Where(x => x.ChannelId == channel.Id)
                               .Select(x => new { x.AllowMask, x.DenyMask })
                               .FirstOrDefaultAsyncLinqToDB();

        var allowMask = current.AllowValue & LOCK_MASK;
        var denyMask = current.DenyValue & LOCK_MASK;

        // someone undid an earlier lock only in part, so the original from before that lock stays
        if (saved is not null && (denyMask & ~saved.DenyMask) != 0)
        {
            allowMask = saved.AllowMask;
            denyMask = saved.DenyMask;
        }

        await channel.AddPermissionOverwriteAsync(everyone,
            new(current.AllowValue & ~LOCK_MASK, current.DenyValue | LOCK_MASK));

        try
        {
            await table.InsertOrUpdateAsync(() => new()
                {
                    GuildId = channel.GuildId,
                    ChannelId = channel.Id,
                    AllowMask = allowMask,
                    DenyMask = denyMask
                },
                _ => new()
                {
                    AllowMask = allowMask,
                    DenyMask = denyMask
                },
                () => new()
                {
                    ChannelId = channel.Id
                });
        }
        catch
        {
            // without the saved row, .unlock can not restore the original overwrite
            try
            {
                await RestoreOverwriteInternalAsync(channel, everyone, current.AllowValue, current.DenyValue);
            }
            catch (Exception rollbackEx)
            {
                Log.Error(rollbackEx, "Unable to undo the lock of channel {ChannelId} after a failed save", channel.Id);
            }

            throw;
        }

        return ChannelLockResult.Success;
    }

    private static async Task RestoreOverwriteInternalAsync(ITextChannel channel, IRole everyone, ulong allow, ulong deny)
    {
        if (allow == 0 && deny == 0)
            await channel.RemovePermissionOverwriteAsync(everyone);
        else
            await channel.AddPermissionOverwriteAsync(everyone, new(allow, deny));
    }

    public async Task<ChannelLockResult> UnlockAsync(ITextChannel channel)
    {
        var everyone = channel.Guild.EveryoneRole;
        var current = channel.GetPermissionOverwrite(everyone) ?? OverwritePermissions.InheritAll;

        await using var uow = db.GetDbContext();
        var table = uow.GetTable<LockedChannel>();
        var saved = await table.Where(x => x.ChannelId == channel.Id)
                               .Select(x => new { x.AllowMask, x.DenyMask })
                               .FirstOrDefaultAsyncLinqToDB();

        // without a saved row only a full lock is undone, so a second .unlock keeps the restored overwrite
        if (saved is null && (current.DenyValue & LOCK_MASK) != LOCK_MASK)
            return ChannelLockResult.NotLocked;

        var allow = (current.AllowValue & ~LOCK_MASK) | (saved?.AllowMask ?? 0);
        var deny = (current.DenyValue & ~LOCK_MASK) | (saved?.DenyMask ?? 0);

        await RestoreOverwriteInternalAsync(channel, everyone, allow, deny);

        if (saved is not null)
            await table.Where(x => x.ChannelId == channel.Id).DeleteAsync();

        return ChannelLockResult.Success;
    }
}
