using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Permissions.Services;

public sealed class CmdCdService : IExecPreCommand, IReadyExecutor, INService
{
    private readonly DbService _db;
    private ConcurrentDictionary<ulong, ConcurrentDictionary<string, int>> _settings = new();

    private readonly ConcurrentDictionary<(ulong, string), ConcurrentDictionary<ulong, DateTime>> _activeCooldowns =
        new();

    public int Priority
        => 0;

    private readonly ShardData _shardData;

    public CmdCdService(DbService db, ShardData shardData)
    {
        _db = db;
        _shardData = shardData;
    }

    public Task<bool> ExecPreCommandAsync(ICommandContext context, string moduleName, CommandInfo command)
        => TryBlock(context.Guild, context.User, command.Name.ToLowerInvariant());

    public Task<bool> TryBlock(IGuild? guild, IUser user, string commandName)
    {
        if (guild is null)
            return Task.FromResult(false);

        if (!_settings.TryGetValue(guild.Id, out var cooldownSettings))
            return Task.FromResult(false);

        if (!cooldownSettings.TryGetValue(commandName, out var cdSeconds))
            return Task.FromResult(false);

        var cooldowns = _activeCooldowns.GetOrAdd(
            (guild.Id, commandName),
            static _ => new());

        // if user is not already on cooldown, add 
        if (cooldowns.TryAdd(user.Id, DateTime.UtcNow))
        {
            return Task.FromResult(false);
        }

        // if there is an entry, maybe it expired. Try to check if it expired and don't fail if it did
        // - just update
        if (cooldowns.TryGetValue(user.Id, out var oldValue))
        {
            var diff = DateTime.UtcNow - oldValue;
            if (diff.TotalSeconds > cdSeconds)
            {
                if (cooldowns.TryUpdate(user.Id, DateTime.UtcNow, oldValue))
                    return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public async Task OnReadyAsync()
    {
        await using (var uow = _db.GetDbContext())
        {
            _settings = await uow.GetTable<CommandCooldown>()
                                 .Where(
                                     x => Queries.GuildOnShard(x.GuildId, _shardData.TotalShards, _shardData.ShardId))
                                 .ToListAsyncLinqToDB()
                                 .Fmap(cmdcds => cmdcds
                                                     .GroupBy(x => x.GuildId)
                                                     .ToDictionary(x => x.Key,
                                                         x => x
                                                              .DistinctBy(x => x.CommandName.ToLower())
                                                              .ToDictionary(y => y.CommandName, y => y.Seconds)
                                                              .ToConcurrent())
                                                     .ToConcurrent());
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync())
        {
            // once per hour delete expired entries
            foreach (var ((guildId, commandName), dict) in _activeCooldowns)
            {
                // if this pair no longer has associated config, that means it has been removed.
                // remove all cooldowns
                if (!_settings.TryGetValue(guildId, out var inner)
                    || !inner.TryGetValue(commandName, out var cdSeconds))
                {
                    _activeCooldowns.Remove((guildId, commandName), out _);
                    continue;
                }

                Cleanup(dict, cdSeconds);
            }
        }
    }

    private void Cleanup(ConcurrentDictionary<ulong, DateTime> dict, int cdSeconds)
    {
        var now = DateTime.UtcNow;
        foreach (var (key, _) in dict.Where(x => (now - x.Value).TotalSeconds > cdSeconds).ToArray())
        {
            dict.TryRemove(key, out _);
        }
    }

    public async Task ClearCooldowns(ulong guildId, string cmdName)
    {
        if (_settings.TryGetValue(guildId, out var dict))
            dict.TryRemove(cmdName, out _);

        _activeCooldowns.TryRemove((guildId, cmdName), out _);

        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<CommandCooldown>()
                 .Where(x => x.GuildId == guildId && x.CommandName == cmdName)
                 .DeleteAsync();
    }

    public async Task AddCooldown(ulong guildId, string name, int secs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secs);

        var sett = _settings.GetOrAdd(guildId, static _ => new());
        sett[name] = secs;

        // force cleanup 
        if (_activeCooldowns.TryGetValue((guildId, name), out var dict))
            Cleanup(dict, secs);

        await using var ctx = _db.GetDbContext();
        await ctx.GetTable<CommandCooldown>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         CommandName = name,
                         Seconds = secs
                     },
                     (old) => new()
                     {
                         Seconds = secs
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         CommandName = name,
                     });
    }

    public IReadOnlyCollection<(string CommandName, int Seconds)> GetCommandCooldowns(ulong guildId)
    {
        if (!_settings.TryGetValue(guildId, out var dict))
            return Array.Empty<(string, int)>();

        return dict.Select(x => (x.Key, x.Value)).ToArray();
    }
}