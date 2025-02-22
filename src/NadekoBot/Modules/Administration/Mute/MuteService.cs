#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration.Services;

public enum MuteType
{
    Voice,
    Chat,
    All
}

public class MuteService : INService, IReadyExecutor
{
    public enum TimerType { Mute, Ban, AddRole }

    private static readonly OverwritePermissions _denyOverwrite = new(addReactions: PermValue.Deny,
        sendMessages: PermValue.Deny,
        sendMessagesInThreads: PermValue.Deny,
        attachFiles: PermValue.Deny);

    public event Action<IGuildUser, IUser, MuteType, string> UserMuted = delegate { };
    public event Action<IGuildUser, IUser, MuteType, string> UserUnmuted = delegate { };

    private ConcurrentDictionary<ulong, string> _guildMuteRoles = new();
    private ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> _mutedUsers = new();

    public ConcurrentDictionary<ulong, ConcurrentDictionary<(ulong, TimerType), Timer>> UnTimers { get; } = new();

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

    private void OnUserMuted(
        IGuildUser user,
        IUser mod,
        MuteType type,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        _ = Task.Run(() => _sender.Response(user)
                                  .Embed(_sender.CreateEmbed(user?.GuildId)
                                                .WithDescription($"You've been muted in {user.Guild} server")
                                                .AddField("Mute Type", type.ToString())
                                                .AddField("Moderator", mod.ToString())
                                                .AddField("Reason", reason))
                                  .SendAsync());
    }

    private void OnUserUnmuted(
        IGuildUser user,
        IUser mod,
        MuteType type,
        string reason)
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
        try
        {
            _mutedUsers.TryGetValue(usr.Guild.Id, out var muted);

            if (muted is null || !muted.Contains(usr.Id))
                return Task.CompletedTask;
            _ = Task.Run(() => MuteUser(usr, _client.CurrentUser, reason: "Sticky mute"));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in MuteService UserJoined event");
        }

        return Task.CompletedTask;
    }

    public async Task SetMuteRoleAsync(ulong guildId, string name)
    {
        await using var uow = _db.GetDbContext();
        var config = uow.GetTable<GuildConfig>()
                        .Where(x => x.GuildId == guildId)
                        .FirstOrDefault();
        config.MuteRoleName = name;
        _guildMuteRoles.AddOrUpdate(guildId, name, (_, _) => name);
        await uow.SaveChangesAsync();
    }

    public async Task MuteUser(
        IGuildUser usr,
        IUser mod,
        MuteType type = MuteType.All,
        string reason = "")
    {
        if (type == MuteType.All)
        {
            try
            { await usr.ModifyAsync(x => x.Mute = true); }
            catch { }

            var muteRole = await GetMuteRole(usr.Guild);
            if (!usr.RoleIds.Contains(muteRole.Id))
                await usr.AddRoleAsync(muteRole);
            StopTimer(usr.GuildId, usr.Id, TimerType.Mute);
            await using (var uow = _db.GetDbContext())
            {
                await uow.GetTable<MutedUserId>()
                         .InsertOrUpdateAsync(() => new()
                         {
                             GuildId = usr.GuildId,
                             UserId = usr.Id
                         },
                             (_) => new()
                             {
                             },
                             () => new()
                             {
                                 GuildId = usr.GuildId,
                                 UserId = usr.Id
                             });

                if (_mutedUsers.TryGetValue(usr.Guild.Id, out var muted))
                    muted.Add(usr.Id);

                await uow.SaveChangesAsync();
            }

            UserMuted(usr, mod, MuteType.All, reason);
        }
        else if (type == MuteType.Voice)
        {
            try
            {
                await usr.ModifyAsync(x => x.Mute = true);
                UserMuted(usr, mod, MuteType.Voice, reason);
            }
            catch { }
        }
        else if (type == MuteType.Chat)
        {
            await usr.AddRoleAsync(await GetMuteRole(usr.Guild));
            UserMuted(usr, mod, MuteType.Chat, reason);
        }
    }

    public async Task UnmuteUser(
        ulong guildId,
        ulong usrId,
        IUser mod,
        MuteType type = MuteType.All,
        string reason = "")
    {
        var usr = _client.GetGuild(guildId)?.GetUser(usrId);
        if (type == MuteType.All)
        {
            StopTimer(guildId, usrId, TimerType.Mute);
            await using (var uow = _db.GetDbContext())
            {
                await uow.GetTable<MutedUserId>()
                         .Where(x => x.GuildId == guildId && x.UserId == usrId)
                         .DeleteAsync();

                await uow.GetTable<UnmuteTimer>()
                         .Where(x => x.GuildId == guildId && x.UserId == usrId)
                         .DeleteAsync();

                if (_mutedUsers.TryGetValue(guildId, out var muted))
                    muted.TryRemove(usrId);
            }

            if (usr is not null)
            {
                try
                { await usr.ModifyAsync(x => x.Mute = false); }
                catch { }

                try
                { await usr.RemoveRoleAsync(await GetMuteRole(usr.Guild)); }
                catch
                {
                    /*ignore*/
                }

                UserUnmuted(usr, mod, MuteType.All, reason);
            }
        }
        else if (type == MuteType.Voice)
        {
            if (usr is null)
                return;

            await usr.ModifyAsync(x => x.Mute = false);
            UserUnmuted(usr, mod, MuteType.Voice, reason);
        }
        else if (type == MuteType.Chat)
        {
            if (usr is null)
                return;
            await usr.RemoveRoleAsync(await GetMuteRole(usr.Guild));
            UserUnmuted(usr, mod, MuteType.Chat, reason);
        }
    }

    public async Task<IRole> GetMuteRole(IGuild guild)
    {
        ArgumentNullException.ThrowIfNull(guild);

        const string defaultMuteRoleName = "nadeko-mute";

        var muteRoleName = _guildMuteRoles.GetOrAdd(guild.Id, defaultMuteRoleName);

        var muteRole = guild.Roles.FirstOrDefault(r => r.Name == muteRoleName);
        if (muteRole is null)
        //if it doesn't exist, create it
        {
            try
            { muteRole = await guild.CreateRoleAsync(muteRoleName, isMentionable: false); }
            catch
            {
                //if creations fails,  maybe the name is not correct, find default one, if doesn't work, create default one
                muteRole = guild.Roles.FirstOrDefault(r => r.Name == muteRoleName)
                           ?? await guild.CreateRoleAsync(defaultMuteRoleName, isMentionable: false);
            }
        }

        foreach (var toOverwrite in await guild.GetTextChannelsAsync())
        {
            try
            {
                if (!toOverwrite.PermissionOverwrites.Any(x => x.TargetId == muteRole.Id
                                                               && x.TargetType == PermissionTarget.Role))
                {
                    await toOverwrite.AddPermissionOverwriteAsync(muteRole, _denyOverwrite);

                    await Task.Delay(200);
                }
            }
            catch
            {
                // ignored
            }
        }

        return muteRole;
    }

    public async Task TimedMute(
        IGuildUser user,
        IUser mod,
        TimeSpan after,
        MuteType muteType = MuteType.All,
        string reason = "")
    {
        await MuteUser(user, mod, muteType, reason); // mute the user. This will also remove any previous unmute timers
        await using (var uow = _db.GetDbContext())
        {
            var unmuteAt = DateTime.UtcNow + after;
            await uow.GetTable<UnmuteTimer>()
                     .InsertAsync(() => new()
                     {
                         GuildId = user.GuildId,
                         UserId = user.Id,
                         UnmuteAt = unmuteAt
                     });
        }

        StartUn_Timer(user.GuildId, user.Id, after, TimerType.Mute); // start the timer
    }

    public async Task TimedBan(
        IGuild guild,
        ulong userId,
        TimeSpan after,
        string reason,
        int pruneDays)
    {
        await guild.AddBanAsync(userId, pruneDays, reason);
        await using (var uow = _db.GetDbContext())
        {
            var unbanAt = DateTime.UtcNow + after;
            await uow.GetTable<UnbanTimer>()
                     .InsertAsync(() => new()
                     {
                         GuildId = guild.Id,
                         UserId = userId,
                         UnbanAt = unbanAt
                     });
        }

        StartUn_Timer(guild.Id, userId, after, TimerType.Ban); // start the timer
    }

    // todo UN* unrole timers -> temprole

    public void StartUn_Timer(
        ulong guildId,
        ulong userId,
        TimeSpan after,
        TimerType type,
        ulong? roleId = null)
    {
        //load the unmute timers for this guild
        var userUnTimers = UnTimers.GetOrAdd(guildId, new ConcurrentDictionary<(ulong, TimerType), Timer>());

        //unmute timer to be added
        var toAdd = new Timer(async _ =>
            {
                if (type == TimerType.Ban)
                {
                    try
                    {
                        await RemoveTimerFromDb(guildId, userId, type);
                        StopTimer(guildId, userId, type);
                        var guild = _client.GetGuild(guildId); // load the guild
                        if (guild is not null)
                            await guild.RemoveBanAsync(userId);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Couldn't unban user {UserId} in guild {GuildId}", userId, guildId);
                    }
                }
                else if (type == TimerType.AddRole)
                {
                    try
                    {
                        if (roleId is null)
                            return;

                        await RemoveTimerFromDb(guildId, userId, type);
                        StopTimer(guildId, userId, type);
                        var guild = _client.GetGuild(guildId);
                        var user = guild?.GetUser(userId);
                        var role = guild?.GetRole(roleId.Value);
                        if (guild is not null && user is not null && user.Roles.Contains(role))
                            await user.RemoveRoleAsync(role);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Couldn't remove role from user {UserId} in guild {GuildId}", userId, guildId);
                    }
                }
                else
                {
                    try
                    {
                        // unmute the user, this will also remove the timer from the db
                        await UnmuteUser(guildId, userId, _client.CurrentUser, reason: "Timed mute expired");
                    }
                    catch (Exception ex)
                    {
                        await RemoveTimerFromDb(guildId, userId, type); // if unmute errored, just remove unmute from db
                        Log.Warning(ex, "Couldn't unmute user {UserId} in guild {GuildId}", userId, guildId);
                    }
                }
            },
            null,
            after,
            Timeout.InfiniteTimeSpan);

        //add it, or stop the old one and add this one
        userUnTimers.AddOrUpdate((userId, type),
            _ => toAdd,
            (_, old) =>
            {
                old.Change(Timeout.Infinite, Timeout.Infinite);
                return toAdd;
            });
    }

    public void StopTimer(ulong guildId, ulong userId, TimerType type)
    {
        if (!UnTimers.TryGetValue(guildId, out var userTimer))
            return;

        if (userTimer.TryRemove((userId, type), out var removed))
            removed.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task RemoveTimerFromDb(ulong guildId, ulong userId, TimerType type)
    {
        using var uow = _db.GetDbContext();
        await using var ctx = _db.GetDbContext();
    }


    // todo UN* update to new way of tracking expiries
    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        var configs = await uow.Set<GuildConfig>()
                               .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                               .ToListAsyncLinqToDB();

        _guildMuteRoles = configs.Where(c => !string.IsNullOrWhiteSpace(c.MuteRoleName))
                                .ToDictionary(c => c.GuildId, c => c.MuteRoleName)
                                .ToConcurrent();

        _mutedUsers = await uow.GetTable<MutedUserId>()
                              .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                              .ToListAsyncLinqToDB()
                              .Fmap(x => x.GroupBy(x => x.GuildId)
                                               .ToDictionary(g => g.Key, g => new ConcurrentHashSet<ulong>(g.Select(x => x.UserId)))
                                               .ToConcurrent());

        var max = TimeSpan.FromDays(49);

        var unmuteTimers = await uow.GetTable<UnmuteTimer>()
                                    .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                                    .ToListAsyncLinqToDB();

        var unbanTimers = await uow.GetTable<UnbanTimer>()
                                    .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                                    .ToListAsyncLinqToDB();

        var unroleTimers = await uow.GetTable<UnroleTimer>()
                                    .Where(x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                                    .ToListAsyncLinqToDB();

        foreach (var conf in configs)
        {
            foreach (var x in unmuteTimers)
            {
                TimeSpan after;
                if (x.UnmuteAt - TimeSpan.FromMinutes(2) <= DateTime.UtcNow)
                {
                    after = TimeSpan.FromMinutes(2);
                }
                else
                {
                    var unmute = x.UnmuteAt - DateTime.UtcNow;
                    after = unmute > max ? max : unmute;
                }

                StartUn_Timer(conf.GuildId, x.UserId, after, TimerType.Mute);
            }

            foreach (var x in unbanTimers)
            {
                TimeSpan after;
                if (x.UnbanAt - TimeSpan.FromMinutes(2) <= DateTime.UtcNow)
                {
                    after = TimeSpan.FromMinutes(2);
                }
                else
                {
                    var unban = x.UnbanAt - DateTime.UtcNow;
                    after = unban > max ? max : unban;
                }

                StartUn_Timer(conf.GuildId, x.UserId, after, TimerType.Ban);
            }

            foreach (var x in unroleTimers)
            {
                TimeSpan after;
                if (x.UnbanAt - TimeSpan.FromMinutes(2) <= DateTime.UtcNow)
                {
                    after = TimeSpan.FromMinutes(2);
                }
                else
                {
                    var unban = x.UnbanAt - DateTime.UtcNow;
                    after = unban > max ? max : unban;
                }

                StartUn_Timer(conf.GuildId, x.UserId, after, TimerType.AddRole, x.RoleId);
            }
        }

        _client.UserJoined += Client_UserJoined;
    }
}