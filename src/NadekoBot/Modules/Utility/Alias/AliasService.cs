using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.Services;

public class AliasService : IInputTransformer, IReadyExecutor, INService
{
    private ConcurrentDictionary<ulong, ConcurrentDictionary<string, string>> _aliases = new();

    private readonly DbService _db;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;

    public AliasService(
        DbService db,
        IMessageSenderService sender,
        ShardData shardData)
    {
        _sender = sender;
        _shardData = shardData;

        using var uow = db.GetDbContext();


        _db = db;
    }

    public async Task<int> ClearAliases(ulong guildId)
    {
        _aliases.TryRemove(guildId, out _);

        await using var uow = _db.GetDbContext();

        var deleted = await uow.GetTable<CommandAlias>()
                               .Where(x => x.GuildId == guildId)
                               .DeleteAsync();

        return deleted;
    }

    public async Task<string?> TransformInput(
        IGuild? guild,
        IMessageChannel channel,
        IUser user,
        string input)
    {
        if (guild is null || string.IsNullOrWhiteSpace(input))
            return null;

        if (_aliases.TryGetValue(guild.Id, out var maps))
        {
            string? newInput = null;

            if (maps.TryGetValue(input, out var alias))
            {
                newInput = alias;
            }
            else
            {
                foreach (var (k, v) in maps)
                {
                    if (string.Equals(input, k, StringComparison.OrdinalIgnoreCase))
                    {
                        newInput = v;
                    }
                    else if (input.StartsWith(k + ' ', StringComparison.OrdinalIgnoreCase))
                    {
                        if (v.Contains("%target%"))
                            newInput = v.Replace("%target%", input[k.Length..]);
                        else
                            newInput = v + ' ' + input[k.Length..];
                    }
                }
            }

            if (newInput is not null)
            {
                try
                {
                    var toDelete = await _sender.Response(channel)
                                                .Confirm($"{input} => {newInput}")
                                                .SendAsync();
                    toDelete.DeleteAfter(1.5f);
                }
                catch
                {
                    // ignored
                }

                return newInput;
            }

            return null;
        }

        return null;
    }

    public async Task OnReadyAsync()
    {
        await using var ctx = _db.GetDbContext();

        var aliases = ctx.GetTable<CommandAlias>()
                         .Where(x => Queries.GuildOnShard(x.GuildId,
                             _shardData.TotalShards,
                             _shardData.ShardId))
                         .ToList();

        _aliases = new();
        foreach (var alias in aliases)
        {
            _aliases.GetOrAdd(alias.GuildId, _ => new(StringComparer.OrdinalIgnoreCase))
                    .TryAdd(alias.Trigger, alias.Mapping);
        }
    }

    public async Task<bool> RemoveAliasAsync(ulong guildId, string trigger)
    {
        await using var ctx = _db.GetDbContext();

        var deleted = await ctx.GetTable<CommandAlias>()
                               .Where(x => x.GuildId == guildId && x.Trigger == trigger)
                               .DeleteAsync();

        if (_aliases.TryGetValue(guildId, out var aliases))
            aliases.TryRemove(trigger, out _);

        return deleted > 0;
    }

    public async Task AddAliasAsync(ulong guildId, string trigger, string mapping)
    {
        await using var ctx = _db.GetDbContext();

        await ctx.GetTable<CommandAlias>()
                 .InsertOrUpdateAsync(() => new()
                     {
                         GuildId = guildId,
                         Trigger = trigger,
                         Mapping = mapping,
                     },
                     (old) => new()
                     {
                         Mapping = mapping
                     },
                     () => new()
                     {
                         GuildId = guildId,
                         Trigger = trigger,
                     });

        var guildDict = _aliases.GetOrAdd(guildId, (_) => new());
        guildDict[trigger] = mapping;
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetAliasesAsync(ulong guildId)
    {
        await Task.Yield();
        
        if (_aliases.TryGetValue(guildId, out var aliases))
            return aliases;

        return null;
    }
}