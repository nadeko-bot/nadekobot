using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Utility.Common;
using NadekoBot.Modules.Utility.Common.Exceptions;
using NadekoBot.Db.Models;
using System.Net;
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

        _ = Task.Run(async () =>
        {
            if (oldPresence?.Activities?.Count != newPresence?.Activities?.Count)
            {
                var guildUsers = _client.Guilds
                                        .Select(x => x.GetUser(user.Id))
                                        .Where(x => x is not null);

                foreach (var guildUser in guildUsers)
                {
                    if (_guildSettings.TryGetValue(guildUser.Guild.Id, out var s))
                        await RescanUser(guildUser, s);
                }
            }
        });

        return Task.CompletedTask;
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();

        _guildSettings = await uow.GetTable<StreamRoleSettings>()
                                 .Where(x => x.Enabled)
                                 .ToDictionaryAsyncLinqToDB(x => x.GuildId, x => x)
                                 .Fmap(x => x.ToConcurrent());

        _client.PresenceUpdated += OnPresenceUpdate;



        await Task.WhenAll(_client.Guilds.Select(RescanUsers).WhenAll(), _queueRunner.RunAsync());
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
            var streamRoleSettings = await uow.GetOrCreateStreamRoleSettings(guild.Id);

            if (listType == StreamRoleListType.Whitelist)
            {
                var userObj = new StreamRoleWhitelistedUser
                {
                    UserId = userId,
                    Username = userName
                };

                if (action == AddRemove.Rem)
                {
                    var toDelete = streamRoleSettings.Whitelist.FirstOrDefault(x => x.Equals(userObj));
                    if (toDelete is not null)
                    {
                        uow.Remove(toDelete);
                        success = true;
                    }
                }
                else
                {
                    success = streamRoleSettings.Whitelist.Add(userObj);
                }
            }
            else
            {
                var userObj = new StreamRoleBlacklistedUser
                {
                    UserId = userId,
                    Username = userName
                };

                if (action == AddRemove.Rem)
                {
                    var toRemove = streamRoleSettings.Blacklist.FirstOrDefault(x => x.Equals(userObj));
                    if (toRemove is not null)
                        success = streamRoleSettings.Blacklist.Remove(toRemove);
                }
                else
                {
                    success = streamRoleSettings.Blacklist.Add(userObj);
                }
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

        if (_guildSettings.TryRemove(guild.Id, out _) && cleanup)
            await RescanUsers(guild);
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
                   || a.Name.ToUpperInvariant().Contains(setting.Keyword.ToUpperInvariant())
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