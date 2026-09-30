using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.Common;
using NadekoBot.Modules.Utility.Common.Exceptions;
using NadekoBot.Db.Models;
using System.Net;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;

namespace NadekoBot.Modules.Utility.Services;

public class StreamRoleService : IReadyExecutor, INService
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private ConcurrentDictionary<ulong, StreamRoleSettings> _guildSettings = new();
    private QueueRunner _queueRunner;

    public StreamRoleService(DiscordSocketClient client, DbService db)
    {
        _db = db;
        _client = client;
        _queueRunner = new QueueRunner();
    }

    private Task OnPresenceUpdate(SocketUser user, SocketPresence? oldPresence, SocketPresence? newPresence)
    {
        if (_guildSettings.IsEmpty
            || user.IsBot
            || !StreamChanged(oldPresence?.Activities, newPresence?.Activities))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            foreach (var (guildId, setting) in _guildSettings)
            {
                if (_client.GetGuild(guildId)?.GetUser(user.Id) is { } guildUser)
                    await RescanUser(guildUser, setting);
            }
        });

        return Task.CompletedTask;
    }

    public static StreamingGame? GetStream(IReadOnlyCollection<IActivity>? activities)
    {
        if (activities is null)
            return null;

        foreach (var activity in activities)
        {
            if (activity is StreamingGame sg)
                return sg;
        }

        return null;
    }

    // the number of activities often stays the same when a stream replaces a game, so compare the streams
    public static bool StreamChanged(IReadOnlyCollection<IActivity>? before, IReadOnlyCollection<IActivity>? after)
    {
        var oldStream = GetStream(before);
        var newStream = GetStream(after);

        if (oldStream is null || newStream is null)
            return oldStream != newStream;

        return !string.Equals(oldStream.Name, newStream.Name, StringComparison.Ordinal)
               || !string.Equals(oldStream.Url, newStream.Url, StringComparison.Ordinal);
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            _guildSettings = await uow.GetTable<StreamRoleSettings>()
                .Where(x => x.Enabled)
                .LoadWith(x => x.Whitelist)
                .LoadWith(x => x.Blacklist)
                .ToDictionaryAsyncLinqToDB(x => x.GuildId, x => x)
                .Pipe(x => x.ToConcurrent());
        }

        _client.PresenceUpdated += OnPresenceUpdate;

        _ = Task.Run(() => _queueRunner.RunAsync());

        foreach (var (guildId, _) in _guildSettings)
        {
            if (_client.GetGuild(guildId) is { } guild)
                await RescanUsers(guild);
        }
    }

    /// <summary>
    ///     Adds or removes a user from a blacklist or a whitelist in the specified guild.
    /// </summary>
    /// <param name="listType">List type</param>
    /// <param name="guild">Guild</param>
    /// <param name="action">Add or rem action</param>
    /// <param name="userId">User's Id</param>
    /// <param name="userName">User's name</param>
    /// <returns>Whether the operation was successful</returns>
    public async Task<bool> ApplyListAction(
        StreamRoleListType listType,
        IGuild guild,
        AddRemove action,
        ulong userId,
        string userName)
    {
        ArgumentNullException.ThrowIfNull(userName, nameof(userName));

        var success = false;
        await using (var uow = _db.GetDbContext())
        {
            // deleted before loading, so every duplicate row goes and the loaded lists are already correct
            if (action == AddRemove.Rem)
            {
                var guildId = guild.Id;
                var deleted = listType == StreamRoleListType.Whitelist
                    ? await uow.GetTable<StreamRoleWhitelistedUser>()
                        .Where(x => x.UserId == userId && x.StreamRoleSettings.GuildId == guildId)
                        .DeleteAsync()
                    : await uow.GetTable<StreamRoleBlacklistedUser>()
                        .Where(x => x.UserId == userId && x.StreamRoleSettings.GuildId == guildId)
                        .DeleteAsync();

                success = deleted > 0;
            }

            var streamRoleSettings = await uow.GetOrCreateStreamRoleSettings(guild.Id);

            if (action == AddRemove.Add)
            {
                success = listType == StreamRoleListType.Whitelist
                    ? streamRoleSettings.Whitelist.Add(new()
                    {
                        UserId = userId,
                        Username = userName
                    })
                    : streamRoleSettings.Blacklist.Add(new()
                    {
                        UserId = userId,
                        Username = userName
                    });
            }

            await uow.SaveChangesAsync();
            UpdateCache(guild.Id, streamRoleSettings);
        }

        if (success)
            await RescanUsers(guild);
        return success;
    }

    /// <summary>
    ///     Sets keyword on a guild and updates the cache.
    /// </summary>
    /// <param name="guild">Guild Id</param>
    /// <param name="keyword">Keyword to set</param>
    /// <returns>The keyword set</returns>
    public async Task<string?> SetKeyword(IGuild guild, string? keyword)
    {
        keyword = keyword?.Trim().ToLowerInvariant();

        await using (var uow = _db.GetDbContext())
        {
            var streamRoleSettings = await uow.GetOrCreateStreamRoleSettings(guild.Id);

            streamRoleSettings.Keyword = keyword;
            UpdateCache(guild.Id, streamRoleSettings);
            await uow.SaveChangesAsync();
        }

        await RescanUsers(guild);
        return keyword;
    }

    /// <summary>
    ///     Gets the currently set keyword on a guild.
    /// </summary>
    /// <param name="guildId">Guild Id</param>
    /// <returns>The keyword set</returns>
    public async Task<string> GetKeyword(ulong guildId)
    {
        if (_guildSettings.TryGetValue(guildId, out var outSetting))
            return outSetting.Keyword;

        StreamRoleSettings setting;
        await using (var uow = _db.GetDbContext())
        {
            setting = await uow.GetOrCreateStreamRoleSettings(guildId);
        }

        UpdateCache(guildId, setting);

        return setting.Keyword;
    }

    /// <summary>
    ///     Sets the role to monitor, and a role to which to add to
    ///     the user who starts streaming in the monitored role.
    /// </summary>
    /// <param name="fromRole">Role to monitor</param>
    /// <param name="addRole">Role to add to the user</param>
    public async Task SetStreamRole(IRole fromRole, IRole addRole)
    {
        ArgumentNullException.ThrowIfNull(fromRole, nameof(fromRole));
        ArgumentNullException.ThrowIfNull(addRole, nameof(addRole));

        StreamRoleSettings setting;
        await using (var uow = _db.GetDbContext())
        {
            var streamRoleSettings = await uow.GetOrCreateStreamRoleSettings(fromRole.Guild.Id);

            streamRoleSettings.Enabled = true;
            streamRoleSettings.AddRoleId = addRole.Id;
            streamRoleSettings.FromRoleId = fromRole.Id;

            setting = streamRoleSettings;
            await uow.SaveChangesAsync();
        }

        UpdateCache(fromRole.Guild.Id, setting);

        foreach (var usr in await fromRole.GetMembersAsync())
        {
            await RescanUser(usr, setting, addRole);
        }
    }

    /// <summary>
    ///     Stops the stream role feature on the specified guild.
    /// </summary>
    /// <param name="guild">Guild</param>
    /// <param name="cleanup">Whether to rescan users</param>
    public async Task StopStreamRole(IGuild guild, bool cleanup = false)
    {
        await using (var uow = _db.GetDbContext())
        {
            var streamRoleSettings = await uow.GetOrCreateStreamRoleSettings(guild.Id);
            streamRoleSettings.Enabled = false;
            streamRoleSettings.AddRoleId = 0;
            streamRoleSettings.FromRoleId = 0;
            await uow.SaveChangesAsync();
        }

        if (!_guildSettings.TryRemove(guild.Id, out var setting) || !cleanup)
            return;

        if (guild.GetRole(setting.AddRoleId) is not { } addRole)
            return;

        var users = await guild.GetUsersAsync(CacheMode.CacheOnly);
        foreach (var user in users)
        {
            if (!user.RoleIds.Contains(addRole.Id))
                continue;

            await _queueRunner.EnqueueAsync(async () =>
            {
                try
                {
                    await user.RemoveRoleAsync(addRole);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed removing the stream role after the feature was disabled");
                }
            });
        }
    }

    private async ValueTask RescanUser(IGuildUser user, StreamRoleSettings setting, IRole? addRole = null)
        => await _queueRunner.EnqueueAsync(() => RescanUserInternal(user, setting, addRole));

    private async Task RescanUserInternal(IGuildUser user, StreamRoleSettings setting, IRole? addRole = null)
    {
        if (user.IsBot)
            return;

        var g = (StreamingGame?)user.Activities.FirstOrDefault(a
            => a is StreamingGame
               && (string.IsNullOrWhiteSpace(setting.Keyword)
                   || a.Name.Contains(setting.Keyword, StringComparison.InvariantCultureIgnoreCase)
                   || setting.Whitelist.Any(x => x.UserId == user.Id)));

        if (g is not null
            && setting.Enabled
            && setting.Blacklist.All(x => x.UserId != user.Id)
            && user.RoleIds.Contains(setting.FromRoleId))
        {
            await _queueRunner.EnqueueAsync(async () =>
            {
                try
                {
                    addRole ??= user.Guild.GetRole(setting.AddRoleId);
                    if (addRole is null)
                    {
                        await StopStreamRole(user.Guild);
                        Log.Warning("Stream role in server {RoleId} no longer exists. Stopping", setting.AddRoleId);
                        return;
                    }

                    //check if he doesn't have addrole already, to avoid errors
                    if (!user.RoleIds.Contains(addRole.Id))
                    {
                        await user.AddRoleAsync(addRole);
                        Log.Information("Added stream role to user {User} in {Server} server",
                            user.ToString(),
                            user.Guild.ToString());
                    }
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    await StopStreamRole(user.Guild);
                    Log.Warning(ex, "Error adding stream role(s). Forcibly disabling stream role feature");
                    throw new StreamRolePermissionException();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed adding stream role");
                }
            });
        }
        else
        {
            //check if user is in the addrole
            if (user.RoleIds.Contains(setting.AddRoleId))
            {
                await _queueRunner.EnqueueAsync(async () =>
                {
                    try
                    {
                        addRole ??= user.Guild.GetRole(setting.AddRoleId);
                        if (addRole is null)
                        {
                            await StopStreamRole(user.Guild);
                            Log.Warning(
                                "Addrole doesn't exist in {GuildId} server. Forcibly disabling stream role feature",
                                user.Guild.Id);
                            return;
                        }

                        // need to check again in case queuer is taking too long to execute
                        if (user.RoleIds.Contains(setting.AddRoleId))
                        {
                            await user.RemoveRoleAsync(addRole);
                        }

                        Log.Information("Removed stream role from the user {User} in {Server} server",
                            user.ToString(),
                            user.Guild.ToString());
                    }
                    catch (HttpException ex)
                    {
                        if (ex.HttpCode == HttpStatusCode.Forbidden)
                        {
                            await StopStreamRole(user.Guild);
                            Log.Warning(ex, "Error removing stream role(s). Forcibly disabling stream role feature");
                        }
                    }
                });
            }
        }
    }

    private async Task RescanUsers(IGuild guild)
    {
        if (!_guildSettings.TryGetValue(guild.Id, out var setting))
            return;

        var addRole = guild.GetRole(setting.AddRoleId);
        if (addRole is null)
            return;

        if (setting.Enabled)
        {
            var users = await guild.GetUsersAsync(CacheMode.CacheOnly);
            foreach (var usr in users.Where(x
                         => x.RoleIds.Contains(setting.FromRoleId) || x.RoleIds.Contains(addRole.Id)))
            {
                if (usr is { } x)
                    await RescanUser(x, setting, addRole);
            }
        }
    }

    private void UpdateCache(ulong guildId, StreamRoleSettings setting)
        => _guildSettings.AddOrUpdate(guildId, _ => setting, (_, _) => setting);
}