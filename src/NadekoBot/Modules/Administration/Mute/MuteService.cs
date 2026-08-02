using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using System.Runtime.ExceptionServices;

namespace NadekoBot.Modules.Administration.Services;

public enum MuteType
{
    Voice,
    Chat,
    All
}

public sealed class MuteService : INService, IReadyExecutor
{
    private const string DEFAULT_MUTE_ROLE_NAME = "nadeko-mute";

    private static readonly TimeSpan _maxTimerDuration = TimeSpan.FromDays(20);
    private static readonly TimeSpan _loopRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _actionCooldown = TimeSpan.FromMilliseconds(500);

    private static readonly OverwritePermissions _denyOverwrite = new(addReactions: PermValue.Deny,
        sendMessages: PermValue.Deny,
        sendMessagesInThreads: PermValue.Deny,
        attachFiles: PermValue.Deny);

    public event Action<IGuildUser, IUser, MuteType, string> UserMuted = delegate { };
    public event Action<IGuildUser, IUser, MuteType, string> UserUnmuted = delegate { };

    private ConcurrentDictionary<ulong, ulong> _guildMuteRoles = new();
    private ConcurrentDictionary<ulong, string> _legacyMuteRoleNames = new();
    private ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> _mutedUsers = new();

    private TaskCompletionSource<bool> _unmuteWake = new();
    private TaskCompletionSource<bool> _unbanWake = new();

    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;

    public MuteService(DiscordSocketClient client, DbService db, IMessageSenderService sender, ShardData shardData)
    {
        _client = client;
        _db = db;
        _sender = sender;
        _shardData = shardData;

        UserMuted += OnUserMuted;
        UserUnmuted += OnUserUnmuted;
    }

    private void OnUserMuted(IGuildUser user, IUser mod, MuteType type, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        _ = Task.Run(() => _sender.Response(user)
            .Embed(_sender.CreateEmbed(user.GuildId)
                .WithDescription($"You've been muted in {user.Guild} server")
                .AddField("Mute Type", type.ToString())
                .AddField("Moderator", mod.ToString())
                .AddField("Reason", reason))
            .SendAsync());
    }

    private void OnUserUnmuted(IGuildUser user, IUser mod, MuteType type, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        _ = Task.Run(() => _sender.Response(user)
            .Embed(_sender.CreateEmbed(user.GuildId)
                .WithDescription($"You've been unmuted in {user.Guild} server")
                .AddField("Unmute Type", type.ToString())
                .AddField("Moderator", mod.ToString())
                .AddField("Reason", reason))
            .SendAsync());
    }

    private Task Client_UserJoined(IGuildUser usr)
    {
        if (_mutedUsers.TryGetValue(usr.Guild.Id, out var muted) && muted.Contains(usr.Id))
            _ = Task.Run(() => MuteChatInternalAsync(usr));

        return Task.CompletedTask;
    }

    public async Task SetMuteRoleAsync(ulong guildId, ulong roleId)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<GuildConfig>()
            .InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    MuteRoleId = roleId,
                    MuteRoleName = null
                },
                _ => new()
                {
                    MuteRoleId = roleId,
                    MuteRoleName = null
                },
                () => new()
                {
                    GuildId = guildId
                });

        _guildMuteRoles[guildId] = roleId;
        _legacyMuteRoleNames.TryRemove(guildId, out _);
    }

    public async Task MuteUser(IGuildUser usr, IUser mod, MuteType type = MuteType.All, string reason = "")
    {
        await ClearUnmuteTimerAsync(usr.GuildId, usr.Id, type);

        // a failure on one surface must not skip the other, or the mute is left half applied
        ExceptionDispatchInfo? chatError = null;
        var anySucceeded = false;

        if (type is MuteType.Chat or MuteType.All)
        {
            try
            {
                await MuteChatInternalAsync(usr);
                anySucceeded = true;
            }
            catch (Exception ex)
            {
                chatError = ExceptionDispatchInfo.Capture(ex);
            }
        }

        if (type is MuteType.Voice or MuteType.All)
            anySucceeded |= await MuteVoiceInternalAsync(usr);

        if (!anySucceeded && chatError is not null)
            chatError.Throw();

        UserMuted(usr, mod, type, reason);
    }

    public async Task UnmuteUser(
        ulong guildId,
        ulong usrId,
        IUser mod,
        MuteType type = MuteType.All,
        string reason = "")
    {
        await ClearUnmuteTimerAsync(guildId, usrId, type);

        ExceptionDispatchInfo? chatError = null;
        var anySucceeded = false;

        if (type is MuteType.Chat or MuteType.All)
        {
            try
            {
                await UnmuteChatInternalAsync(guildId, usrId);
                anySucceeded = true;
            }
            catch (Exception ex)
            {
                chatError = ExceptionDispatchInfo.Capture(ex);
            }
        }

        var usr = _client.GetGuild(guildId)?.GetUser(usrId);
        if (usr is null)
        {
            if (!anySucceeded && chatError is not null)
                chatError.Throw();

            return;
        }

        if (type is MuteType.Voice or MuteType.All)
            anySucceeded |= await UnmuteVoiceInternalAsync(usr);

        if (!anySucceeded && chatError is not null)
            chatError.Throw();

        UserUnmuted(usr, mod, type, reason);
    }

    private async Task MuteChatInternalAsync(IGuildUser usr)
    {
        var muteRole = await GetMuteRole(usr.Guild);
        if (muteRole is null)
            return;

        if (!usr.RoleIds.Contains(muteRole.Id))
            await usr.AddRoleAsync(muteRole);

        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<MutedUserId>()
                .InsertOrUpdateAsync(() => new()
                    {
                        GuildId = usr.GuildId,
                        UserId = usr.Id
                    },
                    _ => new(),
                    () => new()
                    {
                        GuildId = usr.GuildId,
                        UserId = usr.Id
                    });
        }

        _mutedUsers.GetOrAdd(usr.GuildId, static _ => new()).Add(usr.Id);
    }

    private async Task<bool> MuteVoiceInternalAsync(IGuildUser usr)
    {
        try
        {
            await usr.ModifyAsync(x => x.Mute = true);
            return true;
        }
        catch
        {
            // user might not be in a voice channel
            return false;
        }
    }

    private async Task UnmuteChatInternalAsync(ulong guildId, ulong usrId)
    {
        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<MutedUserId>()
                .Where(x => x.GuildId == guildId && x.UserId == usrId)
                .DeleteAsync();
        }

        if (_mutedUsers.TryGetValue(guildId, out var muted))
            muted.TryRemove(usrId);

        var usr = _client.GetGuild(guildId)?.GetUser(usrId);
        if (usr is null)
            return;

        try
        {
            var muteRole = await GetMuteRole(usr.Guild);
            if (muteRole is not null)
                await usr.RemoveRoleAsync(muteRole);
        }
        catch
        {
            // role might have been deleted
        }
    }

    private async Task<bool> UnmuteVoiceInternalAsync(IGuildUser usr)
    {
        try
        {
            await usr.ModifyAsync(x => x.Mute = false);
            return true;
        }
        catch
        {
            // user might not be in a voice channel
            return false;
        }
    }

    public async Task TimedMute(
        IGuildUser user,
        IUser mod,
        TimeSpan after,
        MuteType muteType = MuteType.All,
        string reason = "")
    {
        await MuteUser(user, mod, muteType, reason);

        var unmuteAt = DateTime.UtcNow + after;

        if (muteType is MuteType.Chat or MuteType.All)
            await SetUnmuteTimerAsync(user.GuildId, user.Id, MuteType.Chat, unmuteAt);

        if (muteType is MuteType.Voice or MuteType.All)
            await SetUnmuteTimerAsync(user.GuildId, user.Id, MuteType.Voice, unmuteAt);

        _unmuteWake.TrySetResult(true);
    }

    private async Task SetUnmuteTimerAsync(ulong guildId, ulong userId, MuteType surface, DateTime unmuteAt)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<UnmuteTimer>()
            .InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    UserId = userId,
                    UnmuteAt = unmuteAt,
                    Type = surface
                },
                _ => new()
                {
                    UnmuteAt = unmuteAt
                },
                () => new()
                {
                    GuildId = guildId,
                    UserId = userId,
                    Type = surface
                });
    }

    public async Task TimedBan(IGuild guild, ulong userId, TimeSpan after, string reason, int pruneDays)
    {
        await guild.AddBanAsync(userId, pruneDays, reason);

        var unbanAt = DateTime.UtcNow + after;
        await using (var uow = _db.GetDbContext())
        {
            await uow.GetTable<UnbanTimer>()
                .InsertOrUpdateAsync(() => new()
                    {
                        GuildId = guild.Id,
                        UserId = userId,
                        UnbanAt = unbanAt
                    },
                    _ => new()
                    {
                        UnbanAt = unbanAt
                    },
                    () => new()
                    {
                        GuildId = guild.Id,
                        UserId = userId
                    });
        }

        _unbanWake.TrySetResult(true);
    }

    public async Task<IRole?> GetMuteRole(IGuild guild)
    {
        ArgumentNullException.ThrowIfNull(guild);

        var muteRole = await ResolveMuteRoleInternalAsync(guild);
        if (muteRole is null)
            return null;

        foreach (var channel in await guild.GetTextChannelsAsync())
        {
            if (channel is IThreadChannel)
                continue;

            try
            {
                if (channel.PermissionOverwrites.Any(x => x.TargetId == muteRole.Id
                                                          && x.TargetType == PermissionTarget.Role))
                    continue;

                await channel.AddPermissionOverwriteAsync(muteRole, _denyOverwrite);
                await Task.Delay(200);
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
            {
                Log.Error(ex, "Error initializing mute role in guild {GuildId}: {Message}", guild.Id, ex.Message);
                break;
            }
        }

        return muteRole;
    }

    private async Task<IRole?> ResolveMuteRoleInternalAsync(IGuild guild)
    {
        if (_guildMuteRoles.TryGetValue(guild.Id, out var roleId))
        {
            var byId = guild.GetRole(roleId);
            if (byId is not null)
                return byId;
        }

        var hasLegacyName = _legacyMuteRoleNames.TryGetValue(guild.Id, out var legacyName);
        if (hasLegacyName && FindRoleByName(guild, legacyName!) is { } byName)
        {
            await SetMuteRoleAsync(guild.Id, byName.Id);
            return byName;
        }

        try
        {
            var created = await guild.CreateRoleAsync(hasLegacyName ? legacyName! : DEFAULT_MUTE_ROLE_NAME,
                isMentionable: false);
            await SetMuteRoleAsync(guild.Id, created.Id);
            return created;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to create mute role for guild {GuildId}", guild.Id);
            return null;
        }
    }

    // Role names aren't unique, so prefer the lowest one to keep a squatted higher role from winning.
    public static IRole? FindRoleByName(IGuild guild, string name)
    {
        IRole? found = null;
        foreach (var role in guild.Roles)
        {
            if (!string.Equals(role.Name, name, StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (found is null || role.Position < found.Position)
                found = role;
        }

        return found;
    }

    private async Task ClearUnmuteTimerAsync(ulong guildId, ulong userId, MuteType type)
    {
        await using var uow = _db.GetDbContext();
        var query = uow.GetTable<UnmuteTimer>()
            .Where(x => x.GuildId == guildId && x.UserId == userId);

        if (type != MuteType.All)
            query = query.Where(x => x.Type == type);

        await query.DeleteAsync();
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            var configs = await uow.GetTable<GuildConfig>()
                .Where(Queries.GuildOnShard<GuildConfig>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                .Where(x => x.MuteRoleId != null || x.MuteRoleName != null)
                .Select(x => new
                {
                    x.GuildId,
                    x.MuteRoleId,
                    x.MuteRoleName
                })
                .ToListAsyncLinqToDB();

            _guildMuteRoles = configs.Where(x => x.MuteRoleId is not null)
                .ToDictionary(x => x.GuildId, x => x.MuteRoleId!.Value)
                .ToConcurrent();

            _legacyMuteRoleNames = configs.Where(x => x.MuteRoleId is null && x.MuteRoleName is not null)
                .ToDictionary(x => x.GuildId, x => x.MuteRoleName!)
                .ToConcurrent();

            _mutedUsers = await uow.GetTable<MutedUserId>()
                .Where(Queries.GuildOnShard<MutedUserId>(x => x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                .ToListAsyncLinqToDB()
                .Pipe(x => x.GroupBy(m => m.GuildId)
                    .ToDictionary(g => g.Key, g => new ConcurrentHashSet<ulong>(g.Select(m => m.UserId)))
                    .ToConcurrent());
        }

        _client.UserJoined += Client_UserJoined;

        _ = Task.Run(BackfillMuteRoleIdsInternalAsync);
        _ = Task.Run(UnmuteLoopInternalAsync);
        _ = Task.Run(UnbanLoopInternalAsync);
    }

    private async Task BackfillMuteRoleIdsInternalAsync()
    {
        foreach (var (guildId, name) in _legacyMuteRoleNames)
        {
            try
            {
                var guild = _client.GetGuild(guildId);
                if (guild is null)
                    continue;

                if (FindRoleByName(guild, name) is not { } role)
                    continue;

                await SetMuteRoleAsync(guildId, role.Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to backfill mute role id for guild {GuildId}", guildId);
            }
        }
    }

    private async Task UnmuteLoopInternalAsync()
    {
        while (true)
        {
            try
            {
                _unmuteWake = new(TaskCreationOptions.RunContinuationsAsynchronously);

                DateTime? nextAt;
                await using (var uow = _db.GetDbContext())
                {
                    nextAt = await uow.GetTable<UnmuteTimer>()
                        .Where(Queries.GuildOnShard<UnmuteTimer>(x => x.GuildId,
                            _shardData.TotalShards,
                            _shardData.ShardId))
                        .OrderBy(x => x.UnmuteAt)
                        .Select(x => (DateTime?)x.UnmuteAt)
                        .FirstOrDefaultAsyncLinqToDB();
                }

                if (nextAt is null)
                {
                    await _unmuteWake.Task;
                    continue;
                }

                var now = DateTime.UtcNow;
                if (nextAt.Value > now)
                {
                    var delay = nextAt.Value - now;
                    if (delay > _maxTimerDuration)
                        delay = _maxTimerDuration;

                    await Task.WhenAny(Task.Delay(delay), _unmuteWake.Task);
                    continue;
                }

                UnmuteTimer[] expired;
                await using (var uow = _db.GetDbContext())
                {
                    expired = await uow.GetTable<UnmuteTimer>()
                        .Where(Queries.GuildOnShard<UnmuteTimer>(x => x.GuildId,
                            _shardData.TotalShards,
                            _shardData.ShardId))
                        .Where(x => x.UnmuteAt <= now)
                        .DeleteWithOutputAsync();
                }

                foreach (var timer in expired)
                {
                    try
                    {
                        await UnmuteUser(timer.GuildId,
                            timer.UserId,
                            _client.CurrentUser,
                            timer.Type,
                            "Timed mute expired");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex,
                            "Couldn't unmute user {UserId} in guild {GuildId}",
                            timer.UserId,
                            timer.GuildId);
                    }

                    await Task.Delay(_actionCooldown);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in unmute loop");
                await Task.Delay(_loopRetryDelay);
            }
        }
    }

    private async Task UnbanLoopInternalAsync()
    {
        while (true)
        {
            try
            {
                _unbanWake = new(TaskCreationOptions.RunContinuationsAsynchronously);

                DateTime? nextAt;
                await using (var uow = _db.GetDbContext())
                {
                    nextAt = await uow.GetTable<UnbanTimer>()
                        .Where(Queries.GuildOnShard<UnbanTimer>(x => x.GuildId,
                            _shardData.TotalShards,
                            _shardData.ShardId))
                        .OrderBy(x => x.UnbanAt)
                        .Select(x => (DateTime?)x.UnbanAt)
                        .FirstOrDefaultAsyncLinqToDB();
                }

                if (nextAt is null)
                {
                    await _unbanWake.Task;
                    continue;
                }

                var now = DateTime.UtcNow;
                if (nextAt.Value > now)
                {
                    var delay = nextAt.Value - now;
                    if (delay > _maxTimerDuration)
                        delay = _maxTimerDuration;

                    await Task.WhenAny(Task.Delay(delay), _unbanWake.Task);
                    continue;
                }

                UnbanTimer[] expired;
                await using (var uow = _db.GetDbContext())
                {
                    expired = await uow.GetTable<UnbanTimer>()
                        .Where(Queries.GuildOnShard<UnbanTimer>(x => x.GuildId,
                            _shardData.TotalShards,
                            _shardData.ShardId))
                        .Where(x => x.UnbanAt <= now)
                        .DeleteWithOutputAsync();
                }

                foreach (var timer in expired)
                {
                    try
                    {
                        var guild = _client.GetGuild(timer.GuildId);
                        if (guild is not null)
                            await guild.RemoveBanAsync(timer.UserId);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex,
                            "Couldn't unban user {UserId} in guild {GuildId}",
                            timer.UserId,
                            timer.GuildId);
                    }

                    await Task.Delay(_actionCooldown);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in unban loop");
                await Task.Delay(_loopRetryDelay);
            }
        }
    }
}
