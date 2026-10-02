using System.Net;
using System.Threading.Channels;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public enum TempVoiceHubResult
{
    Added,
    Removed,
    LimitReached,
    IsTempChannel
}

public readonly record struct TempVoiceInfo(ulong GuildId, ulong OwnerId);

public sealed class TempVoiceService(
    DbService db,
    DiscordSocketClient client,
    ShardData shardData,
    IBotStrings strings,
    TimeProvider? timeProvider = null) : IReadyExecutor, INService
{
    public const int MAX_HUBS = 5;
    public const int MAX_CHANNEL_NAME_LENGTH = 100;
    public static readonly TimeSpan CreateCooldown = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DeleteDelay = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ActionGap = TimeSpan.FromSeconds(3);
    public const int MAX_TEMP_CHANNELS_PER_GUILD = 25;
    private const int HUB_WORKERS = 2;
    private const int MAX_QUEUED_JOINS = 500;
    private static readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(1);
    private const int DELETE_EVERY_TICKS = 5;
    private const int PRUNE_EVERY_TICKS = 300;

    private static readonly OverwritePermissions _ownerPerms = new(
        connect: PermValue.Allow,
        manageChannel: PermValue.Allow,
        moveMembers: PermValue.Allow);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    private readonly ConcurrentDictionary<ulong, ulong> _hubs = new();
    private readonly ConcurrentDictionary<ulong, TempVoiceInfo> _temps = new();
    private readonly ConcurrentDictionary<(ulong GuildId, ulong OwnerId), ulong> _owned = new();
    private readonly ConcurrentDictionary<ulong, long> _lastCreated = new();
    private readonly ConcurrentDictionary<ulong, long> _lastAction = new();
    private readonly ConcurrentDictionary<ulong, long> _emptySince = new();
    private readonly ConcurrentDictionary<ulong, int> _guildTempCount = new();

    // a member is in _pending from the hub join until a worker is done, so repeated clicks queue nothing
    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), byte> _pending = new();
    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), long> _delayed = new();

    private readonly Channel<(ulong GuildId, ulong UserId)> _queue =
        Channel.CreateBounded<(ulong GuildId, ulong UserId)>(new BoundedChannelOptions(MAX_QUEUED_JOINS)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

    public async Task OnReadyAsync()
    {
        List<TempVoiceHub> hubs;
        List<TempVoiceChannel> temps;
        await using (var uow = db.GetDbContext())
        {
            hubs = await uow.GetTable<TempVoiceHub>()
                            .Where(Queries.GuildOnShard<TempVoiceHub>(x => x.GuildId,
                                shardData.TotalShards,
                                shardData.ShardId))
                            .ToListAsyncLinqToDB();

            temps = await uow.GetTable<TempVoiceChannel>()
                             .Where(Queries.GuildOnShard<TempVoiceChannel>(x => x.GuildId,
                                 shardData.TotalShards,
                                 shardData.ShardId))
                             .ToListAsyncLinqToDB();
        }

        foreach (var hub in hubs)
            _hubs[hub.ChannelId] = hub.GuildId;

        foreach (var temp in temps)
        {
            TrackTemp(temp.ChannelId, new(temp.GuildId, temp.OwnerId));
            _guildTempCount.AddOrUpdate(temp.GuildId, 1, static (_, c) => c + 1);
        }

        client.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        client.LeftGuild += OnLeftGuildAsync;

        _ = Task.Run(() => SweepInternalAsync(temps));
        _ = Task.Run(TickLoopAsync);

        for (var i = 0; i < HUB_WORKERS; i++)
            _ = Task.Run(HubWorkerLoopAsync);
    }

    // Members can leave a temp channel while the bot is offline, so it is empty with no leave event.
    private async Task SweepInternalAsync(List<TempVoiceChannel> temps)
    {
        var now = _time.GetTimestamp();
        foreach (var temp in temps)
        {
            try
            {
                var guild = client.GetGuild(temp.GuildId);
                if (guild is null)
                    continue;

                var ch = guild.GetVoiceChannel(temp.ChannelId);
                if (ch is null)
                {
                    await ForgetTempInternalAsync(temp.ChannelId);
                    continue;
                }

                if (ch.ConnectedUsers.Count == 0)
                    _emptySince.TryAdd(ch.Id, now);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to clean up temp voice channel {ChannelId}", temp.ChannelId);
            }
        }
    }

    private async Task TickLoopAsync()
    {
        using var timer = new PeriodicTimer(_tickInterval, _time);
        var ticks = 0;
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                ticks++;
                RequeueDelayedInternal();

                if (ticks % DELETE_EVERY_TICKS == 0)
                    await DeleteEmptyChannelsInternalAsync();

                if (ticks % PRUNE_EVERY_TICKS == 0)
                    PruneTimestampsInternal();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in the temp voice loop");
            }
        }
    }

    private void RequeueDelayedInternal()
    {
        var now = _time.GetTimestamp();
        foreach (var (key, last) in _delayed)
        {
            if (RemainingActionDelay(last, now, _time) > TimeSpan.Zero)
                continue;

            if (!_delayed.TryRemove(new KeyValuePair<(ulong, ulong), long>(key, last)))
                continue;

            // the key is still pending, so it goes straight back into the queue
            if (!_queue.Writer.TryWrite(key))
                DropFullQueueInternal(key);
        }
    }

    private void EnqueueHubJoin((ulong GuildId, ulong UserId) key)
    {
        if (!_pending.TryAdd(key, 0))
            return;

        if (!_queue.Writer.TryWrite(key))
            DropFullQueueInternal(key);
    }

    private void DropFullQueueInternal((ulong GuildId, ulong UserId) key)
    {
        _pending.TryRemove(key, out _);
        Log.Warning("Temp voice queue is full, ignoring hub join of user {UserId} in guild {GuildId}",
            key.UserId,
            key.GuildId);
    }

    private async Task HubWorkerLoopAsync()
    {
        await foreach (var key in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await ProcessHubJoinInternalAsync(key);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to handle a temp voice hub join in guild {GuildId}", key.GuildId);
            }
        }
    }

    private async Task ProcessHubJoinInternalAsync((ulong GuildId, ulong UserId) key)
    {
        // the queued event may be old, so act on where the member is now
        var user = client.GetGuild(key.GuildId)?.GetUser(key.UserId);
        if (user?.VoiceChannel is not { } hub || !_hubs.ContainsKey(hub.Id))
        {
            _pending.TryRemove(key, out _);
            return;
        }

        var now = _time.GetTimestamp();
        if (_lastAction.TryGetValue(key.UserId, out var last)
            && RemainingActionDelay(last, now, _time) > TimeSpan.Zero)
        {
            _delayed[key] = last;
            return;
        }

        _lastAction[key.UserId] = now;

        try
        {
            await HandleHubJoinInternalAsync(user, hub);
        }
        finally
        {
            _pending.TryRemove(key, out _);

            // a click during the action was ignored because the key was pending
            if (user.VoiceChannel is { } current && _hubs.ContainsKey(current.Id))
                EnqueueHubJoin(key);
        }
    }

    public static TimeSpan RemainingActionDelay(long lastAction, long now, TimeProvider time)
    {
        var elapsed = time.GetElapsedTime(lastAction, now);
        return elapsed >= ActionGap ? TimeSpan.Zero : ActionGap - elapsed;
    }

    public bool TryReserveSlot(ulong guildId)
    {
        if (_guildTempCount.AddOrUpdate(guildId, 1, static (_, c) => c + 1) <= MAX_TEMP_CHANNELS_PER_GUILD)
            return true;

        ReleaseSlot(guildId);
        return false;
    }

    public void ReleaseSlot(ulong guildId)
        => _guildTempCount.AddOrUpdate(guildId, 0, static (_, c) => c > 0 ? c - 1 : 0);

    private async Task DeleteEmptyChannelsInternalAsync()
    {
        var now = _time.GetTimestamp();
        foreach (var (channelId, since) in _emptySince)
        {
            if (!_temps.TryGetValue(channelId, out var info)
                || client.GetGuild(info.GuildId)?.GetVoiceChannel(channelId) is not { } ch)
            {
                _emptySince.TryRemove(channelId, out _);
                continue;
            }

            if (ch.ConnectedUsers.Count > 0)
            {
                _emptySince.TryRemove(new KeyValuePair<ulong, long>(channelId, since));
                continue;
            }

            if (!IsDueForDelete(since, now, _time))
                continue;

            if (!_emptySince.TryRemove(new KeyValuePair<ulong, long>(channelId, since)))
                continue;

            if (!await ForgetTempInternalAsync(channelId))
                continue;

            try
            {
                await ch.DeleteAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete empty temp voice channel {ChannelId}", channelId);
            }
        }
    }

    public static bool IsDueForDelete(long emptySince, long now, TimeProvider time)
        => time.GetElapsedTime(emptySince, now) >= DeleteDelay;

    private void PruneTimestampsInternal()
    {
        var now = _time.GetTimestamp();
        foreach (var (userId, last) in _lastCreated)
        {
            if (_time.GetElapsedTime(last, now) >= CreateCooldown)
                _lastCreated.TryRemove(new KeyValuePair<ulong, long>(userId, last));
        }

        foreach (var (userId, last) in _lastAction)
        {
            if (_time.GetElapsedTime(last, now) >= ActionGap)
                _lastAction.TryRemove(new KeyValuePair<ulong, long>(userId, last));
        }
    }

    public bool TryStartCooldown(ulong userId)
    {
        var now = _time.GetTimestamp();
        while (true)
        {
            if (_lastCreated.TryGetValue(userId, out var last))
            {
                if (_time.GetElapsedTime(last, now) < CreateCooldown)
                    return false;

                if (_lastCreated.TryUpdate(userId, now, last))
                    return true;
            }
            else if (_lastCreated.TryAdd(userId, now))
            {
                return true;
            }
        }
    }

    private Task OnUserVoiceStateUpdated(SocketUser usr, SocketVoiceState before, SocketVoiceState after)
    {
        if (usr is not SocketGuildUser user)
            return Task.CompletedTask;

        var beforeCh = before.VoiceChannel;
        var afterCh = after.VoiceChannel;

        // the last time a channel becomes empty wins, so a short visit does not shorten the delay
        if (beforeCh is not null
            && beforeCh.Id != afterCh?.Id
            && beforeCh.ConnectedUsers.Count == 0
            && _temps.ContainsKey(beforeCh.Id))
            _emptySince[beforeCh.Id] = _time.GetTimestamp();

        if (afterCh is not null && afterCh.Id != beforeCh?.Id && _hubs.ContainsKey(afterCh.Id))
            EnqueueHubJoin((user.Guild.Id, user.Id));

        return Task.CompletedTask;
    }

    private async Task HandleHubJoinInternalAsync(SocketGuildUser user, SocketVoiceChannel hub)
    {
        if (_owned.TryGetValue((user.Guild.Id, user.Id), out var ownedId)
            && user.Guild.GetVoiceChannel(ownedId) is { } owned)
        {
            await user.ModifyAsync(x => x.Channel = owned);
            _emptySince.TryRemove(ownedId, out _);
            return;
        }

        if (!TryReserveSlot(user.Guild.Id))
            return;

        if (!TryStartCooldown(user.Id))
        {
            ReleaseSlot(user.Guild.Id);
            return;
        }

        var name = TrimName(strings.GetText(strs.tempvoice_channel_name(user.DisplayName), user.Guild.Id));
        var overwrites = BuildOverwrites(hub.PermissionOverwrites, user.Id);

        IVoiceChannel ch;
        try
        {
            ch = await user.Guild.CreateVoiceChannelAsync(name,
                x =>
                {
                    x.CategoryId = hub.CategoryId;
                    x.Bitrate = hub.Bitrate;
                    x.UserLimit = hub.UserLimit;
                    x.PermissionOverwrites = overwrites;
                });
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            ReleaseSlot(user.Guild.Id);
            Log.Warning("Removing temp voice hub {ChannelId} in guild {GuildId}: no permission to create channels",
                hub.Id,
                user.Guild.Id);
            await RemoveHubInternalAsync(hub.Id);
            return;
        }
        catch
        {
            ReleaseSlot(user.Guild.Id);
            throw;
        }

        TrackTemp(ch.Id, new(user.Guild.Id, user.Id));

        await using (var uow = db.GetDbContext())
        {
            await uow.GetTable<TempVoiceChannel>()
                     .InsertAsync(() => new()
                     {
                         GuildId = user.Guild.Id,
                         ChannelId = ch.Id,
                         OwnerId = user.Id
                     });
        }

        try
        {
            await user.ModifyAsync(x => x.Channel = Optional.Create(ch));
        }
        catch (Exception ex)
        {
            // The member left the hub before the move, so nobody will ever leave the new channel.
            Log.Warning(ex, "Failed to move user {UserId} into temp voice channel {ChannelId}", user.Id, ch.Id);
            if (await ForgetTempInternalAsync(ch.Id))
                await ch.DeleteAsync();
        }
    }

    public static string TrimName(string name)
    {
        if (name.Length <= MAX_CHANNEL_NAME_LENGTH)
            return name;

        var length = MAX_CHANNEL_NAME_LENGTH;
        if (char.IsHighSurrogate(name[length - 1]))
            length--;

        return name[..length];
    }

    public static List<Overwrite> BuildOverwrites(IReadOnlyCollection<Overwrite> hubOverwrites, ulong ownerId)
    {
        var result = new List<Overwrite>(hubOverwrites.Count + 1);
        foreach (var ow in hubOverwrites)
        {
            if (ow.TargetType == PermissionTarget.User && ow.TargetId == ownerId)
                continue;

            result.Add(ow);
        }

        result.Add(new(ownerId, PermissionTarget.User, _ownerPerms));
        return result;
    }

    private void TrackTemp(ulong channelId, TempVoiceInfo info)
    {
        _temps[channelId] = info;
        _owned[(info.GuildId, info.OwnerId)] = channelId;
    }

    private void UntrackTemp(ulong channelId, TempVoiceInfo info)
    {
        _owned.TryRemove(new KeyValuePair<(ulong, ulong), ulong>((info.GuildId, info.OwnerId), channelId));
        _emptySince.TryRemove(channelId, out _);
        ReleaseSlot(info.GuildId);
    }

    private async Task<bool> ForgetTempInternalAsync(ulong channelId)
    {
        if (!_temps.TryRemove(channelId, out var info))
            return false;

        UntrackTemp(channelId, info);

        await using var uow = db.GetDbContext();
        await uow.GetTable<TempVoiceChannel>()
                 .Where(x => x.ChannelId == channelId)
                 .DeleteAsync();

        return true;
    }

    private async Task RemoveHubInternalAsync(ulong channelId)
    {
        if (!_hubs.TryRemove(channelId, out _))
            return;

        await using var uow = db.GetDbContext();
        await uow.GetTable<TempVoiceHub>()
                 .Where(x => x.ChannelId == channelId)
                 .DeleteAsync();
    }

    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        await ForgetTempInternalAsync(channel.Id);
        await RemoveHubInternalAsync(channel.Id);
    }

    private async Task OnLeftGuildAsync(SocketGuild guild)
    {
        foreach (var (channelId, guildId) in _hubs)
        {
            if (guildId == guild.Id)
                _hubs.TryRemove(channelId, out _);
        }

        foreach (var (channelId, info) in _temps)
        {
            if (info.GuildId == guild.Id && _temps.TryRemove(channelId, out _))
                UntrackTemp(channelId, info);
        }

        _guildTempCount.TryRemove(guild.Id, out _);

        await using var uow = db.GetDbContext();
        await uow.GetTable<TempVoiceHub>()
                 .Where(x => x.GuildId == guild.Id)
                 .DeleteAsync();
        await uow.GetTable<TempVoiceChannel>()
                 .Where(x => x.GuildId == guild.Id)
                 .DeleteAsync();
    }

    public async Task<TempVoiceHubResult> ToggleHubAsync(ulong guildId, ulong channelId)
    {
        if (_temps.ContainsKey(channelId))
            return TempVoiceHubResult.IsTempChannel;

        await using var uow = db.GetDbContext();
        await using var tx = await uow.Database.BeginTransactionAsync();
        var table = uow.GetTable<TempVoiceHub>();

        var deleted = await table
                            .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
                            .DeleteAsync();

        if (deleted > 0)
        {
            await tx.CommitAsync();
            _hubs.TryRemove(channelId, out _);
            return TempVoiceHubResult.Removed;
        }

        await table.InsertAsync(() => new()
        {
            GuildId = guildId,
            ChannelId = channelId
        });

        // the insert takes the sqlite write lock first, so a concurrent toggle sees this row in its count
        var count = await table.CountAsyncLinqToDB(x => x.GuildId == guildId);
        if (count > MAX_HUBS)
            return TempVoiceHubResult.LimitReached;

        await tx.CommitAsync();
        _hubs[channelId] = guildId;
        return TempVoiceHubResult.Added;
    }

    public async Task<IReadOnlyList<ulong>> GetHubsAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        return await uow.GetTable<TempVoiceHub>()
                        .Where(x => x.GuildId == guildId)
                        .OrderBy(x => x.Id)
                        .Select(x => x.ChannelId)
                        .ToListAsyncLinqToDB();
    }
}
