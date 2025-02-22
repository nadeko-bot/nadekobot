using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using OneOf.Types;
using OneOf;

namespace NadekoBot.Modules.Administration.Services;

public sealed class ReactionRolesService : IReadyExecutor, INService, IReactionRoleService
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly IBotCreds _creds;

    private ConcurrentDictionary<ulong, List<ReactionRoleV2>> _cache;
    private readonly object _cacheLock = new();
    private readonly SemaphoreSlim _assignementLock = new(1, 1);

    public ReactionRolesService(
        DiscordSocketClient client,
        DbService db,
        IBotCreds creds)
    {
        _db = db;
        _client = client;
        _creds = creds;
        _cache = new();
    }

    public async Task OnReadyAsync()
    {
        await using var uow = _db.GetDbContext();
        var reros = await uow.GetTable<ReactionRoleV2>()
                             .Where(
                                 x => Queries.GuildOnShard(x.GuildId, _creds.TotalShards, _client.ShardId))
                             .ToListAsyncLinqToDB();

        foreach (var group in reros.GroupBy(x => x.MessageId))
        {
            _cache[group.Key] = group.ToList();
        }

        _client.ReactionAdded += ClientOnReactionAdded;
        _client.ReactionRemoved += ClientOnReactionRemoved;
    }

    private async Task<(IGuildUser, IRole)> GetUserAndRoleAsync(
        ulong userId,
        ReactionRoleV2 rero)
    {
        var guild = _client.GetGuild(rero.GuildId);
        var role = guild?.GetRole(rero.RoleId);

        if (guild is null || role is null)
            return default;

        var user = guild.GetUser(userId) as IGuildUser
                   ?? await _client.Rest.GetGuildUserAsync(guild.Id, userId);

        if (user is null)
            return default;

        return (user, role);
    }

    private Task ClientOnReactionRemoved(
        Cacheable<IUserMessage, ulong> cmsg,
        Cacheable<IMessageChannel, ulong> ch,
        SocketReaction r)
    {
        if (!_cache.TryGetValue(cmsg.Id, out var reros))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            var emote = await GetFixedEmoteAsync(cmsg, r.Emote);

            var rero = reros.FirstOrDefault(x => x.Emote == emote.Name
                                                 || x.Emote == emote.ToString());
            if (rero is null)
                return;

            var (user, role) = await GetUserAndRoleAsync(r.UserId, rero);

            if (user.IsBot)
                return;

            await _assignementLock.WaitAsync();
            try
            {
                if (user.RoleIds.Contains(role.Id))
                {
                    await user.RemoveRoleAsync(role.Id,
                        new RequestOptions()
                        {
                            AuditLogReason = $"Reaction role"
                        });
                }
            }
            finally
            {
                _assignementLock.Release();
            }
        });

        return Task.CompletedTask;
    }


    // had to add this because for some reason, reactionremoved event's reaction doesn't have IsAnimated set,
    // causing the .ToString() to be wrong on animated custom emotes
    private async Task<IEmote> GetFixedEmoteAsync(
        Cacheable<IUserMessage, ulong> cmsg,
        IEmote inputEmote)
    {
        // this should only run for emote
        if (inputEmote is not Emote e)
            return inputEmote;

        // try to get the message and pull
        var msg = await cmsg.GetOrDownloadAsync();

        var emote = msg.Reactions.Keys.FirstOrDefault(x => e.Equals(x));
        return emote ?? inputEmote;
    }

    private Task ClientOnReactionAdded(
        Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> ch,
        SocketReaction r)
    {
        if (!_cache.TryGetValue(msg.Id, out var reros))
            return Task.CompletedTask;

        _ = Task.Run(async () =>
        {
            var rero = reros.FirstOrDefault(x => x.Emote == r.Emote.Name || x.Emote == r.Emote.ToString());
            if (rero is null)
                return;

            var (user, role) = await GetUserAndRoleAsync(r.UserId, rero);

            if (user.IsBot)
                return;

            await _assignementLock.WaitAsync();
            try
            {
                if (!user.RoleIds.Contains(role.Id))
                {
                    // first check if there is a level requirement
                    // and if there is, make sure user satisfies it
                    if (rero.LevelReq > 0)
                    {
                        await using var ctx = _db.GetDbContext();
                        var levelData = await ctx.GetTable<UserXpStats>()
                                                 .GetLevelDataFor(user.GuildId, user.Id);

                        if (levelData.Level < rero.LevelReq)
                            return;
                    }

                    // remove all other roles from the same group from the user
                    // execept in group 0, which is a special, non-exclusive group
                    if (rero.Group != 0)
                    {
                        var exclusive = reros
                                        .Where(x => x.Group == rero.Group && x.RoleId != role.Id)
                                        .Select(x => x.RoleId)
                                        .Distinct()
                                        .ToArray();


                        if (exclusive.Any())
                        {
                            try
                            {
                                await user.RemoveRolesAsync(exclusive,
                                    new RequestOptions()
                                    {
                                        AuditLogReason = "Reaction role exclusive group"
                                    });
                            }
                            catch { }
                        }

                        // remove user's previous reaction
                        try
                        {
                            var m = await msg.GetOrDownloadAsync();
                            if (m is not null)
                            {
                                var reactToRemove = m.Reactions
                                                     .FirstOrDefault(x => x.Key.ToString() != r.Emote.ToString())
                                                     .Key;

                                if (reactToRemove is not null)
                                {
                                    await m.RemoveReactionAsync(reactToRemove, user);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    await user.AddRoleAsync(role.Id,
                        new()
                        {
                            AuditLogReason = "Reaction role"
                        });
                }
            }
            finally
            {
                _assignementLock.Release();
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a single reaction role
    /// </summary>
    /// <param name="guild">Guild where to add a reaction role</param>
    /// <param name="msg">Message to which to add a reaction role</param>
    /// <param name="emote"></param>
    /// <param name="role"></param>
    /// <param name="group"></param>
    /// <param name="levelReq"></param>
    /// <returns>The result of the operation</returns>
    public async Task<OneOf<Success, Error>> AddReactionRole(
        IGuild guild,
        IMessage msg,
        string emote,
        IRole role,
        int group = 0,
        int levelReq = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(group);
        ArgumentOutOfRangeException.ThrowIfNegative(levelReq);

        await using var ctx = _db.GetDbContext();

        await ctx.GetTable<ReactionRoleV2>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guild.Id,
                         ChannelId = msg.Channel.Id,

                         MessageId = msg.Id,
                         Emote = emote,

                         RoleId = role.Id,
                         Group = group,
                         LevelReq = levelReq
                     },
                     (old) => new()
                     {
                         RoleId = role.Id,
                         Group = group,
                         LevelReq = levelReq
                     },
                     () => new()
                     {
                         MessageId = msg.Id,
                         Emote = emote,
                     });

        var obj = new ReactionRoleV2()
        {
            GuildId = guild.Id,
            MessageId = msg.Id,
            Emote = emote,
            RoleId = role.Id,
            Group = group,
            LevelReq = levelReq
        };

        lock (_cacheLock)
        {
            _cache.AddOrUpdate(msg.Id,
                _ => [obj],
                (_, list) =>
                {
                    list.RemoveAll(x => x.Emote == emote);
                    list.Add(obj);
                    return list;
                });
        }

        return new Success();
    }

    /// <summary>
    /// Get all reaction roles on the specified server
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns></returns>
    public async Task<IReadOnlyCollection<ReactionRoleV2>> GetReactionRolesAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<ReactionRoleV2>()
                        .Where(x => x.GuildId == guildId)
                        .ToListAsync();
    }

    /// <summary>
    /// Remove reaction roles on the specified message
    /// </summary>
    /// <param name="guildId"></param>
    /// <param name="messageId"></param>
    /// <returns></returns>
    public async Task<bool> RemoveReactionRoles(ulong guildId, ulong messageId)
    {
        // guildid is used for quick index lookup
        await using var ctx = _db.GetDbContext();
        var changed = await ctx.GetTable<ReactionRoleV2>()
                               .Where(x => x.GuildId == guildId && x.MessageId == messageId)
                               .DeleteAsync();

        _cache.TryRemove(messageId, out _);

        if (changed == 0)
            return false;

        return true;
    }

    /// <summary>
    /// Remove all reaction roles in the specified server
    /// </summary>
    /// <param name="guildId"></param>
    /// <returns></returns>
    public async Task<int> RemoveAllReactionRoles(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();
        var output = await ctx.GetTable<ReactionRoleV2>()
                              .Where(x => x.GuildId == guildId)
                              .DeleteWithOutputAsync(x => x.MessageId);

        lock (_cacheLock)
        {
            foreach (var o in output)
            {
                _cache.TryRemove(o, out _);
            }
        }

        return output.Length;
    }

    public async Task<IReadOnlyCollection<IEmote>> TransferReactionRolesAsync(
        ulong guildId,
        ulong fromMessageId,
        ulong toMessageId)
    {
        await using var ctx = _db.GetDbContext();
        var updated = ctx.GetTable<ReactionRoleV2>()
                         .Where(x => x.GuildId == guildId && x.MessageId == fromMessageId)
                         .UpdateWithOutput(old => new()
                             {
                                 MessageId = toMessageId
                             },
                             (old, neu) => neu);
        lock (_cacheLock)
        {
            if (_cache.TryRemove(fromMessageId, out var data))
            {
                if (_cache.TryGetValue(toMessageId, out var newData))
                {
                    newData.AddRange(data);
                }
                else
                {
                    _cache[toMessageId] = data;
                }
            }
        }

        return updated.Select(x => x.Emote.ToIEmote()).ToList();
    }
}