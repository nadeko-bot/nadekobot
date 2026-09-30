using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.Services;

public enum AliasAddResult
{
    Added,
    Updated,
    LimitReached
}

public class AliasService : IInputTransformer, IReadyExecutor, INService
{
    public const int MAX_ALIASES = 50;

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

    public async ValueTask<string?> TransformInput(
        IGuild? guild,
        IMessageChannel channel,
        IUser user,
        string input)
    {
        if (guild is null || string.IsNullOrWhiteSpace(input))
            return null;

        if (_aliases.TryGetValue(guild.Id, out var maps))
        {
            var newInput = FindMapping(maps, input);

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

    public static string? FindMapping(ConcurrentDictionary<string, string> maps, string input)
    {
        if (maps.TryGetValue(input, out var exact))
            return exact;

        // the longest trigger wins, so ".alias hi" and ".alias hi all" do not depend on dictionary order
        string? bestTrigger = null;
        string? bestMapping = null;
        foreach (var (trigger, mapping) in maps)
        {
            if (input.Length <= trigger.Length
                || input[trigger.Length] != ' '
                || (bestTrigger is not null && trigger.Length <= bestTrigger.Length)
                || !input.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                continue;

            bestTrigger = trigger;
            bestMapping = mapping;
        }

        if (bestTrigger is null || bestMapping is null)
            return null;

        var rest = input.AsSpan(bestTrigger.Length);
        if (bestMapping.Contains("%target%", StringComparison.Ordinal))
            return bestMapping.Replace("%target%", rest.ToString(), StringComparison.Ordinal);

        return string.Concat(bestMapping, " ", rest);
    }

    public async Task OnReadyAsync()
    {
        await using var ctx = _db.GetDbContext();

        var aliases = await ctx.GetTable<CommandAlias>()
                               .Where(Queries.GuildOnShard<CommandAlias>(x => x.GuildId,
                                   _shardData.TotalShards,
                                   _shardData.ShardId))
                               .ToListAsyncLinqToDB();

        _aliases = new();
        foreach (var alias in aliases)
        {
            _aliases.GetOrAdd(alias.GuildId, static _ => new(StringComparer.OrdinalIgnoreCase))
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

    public async Task<AliasAddResult> AddAliasAsync(ulong guildId, string trigger, string mapping)
    {
        await using (var ctx = _db.GetDbContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var table = ctx.GetTable<CommandAlias>();

            var updated = await table
                .Where(x => x.GuildId == guildId && x.Trigger == trigger)
                .Set(x => x.Mapping, mapping)
                .UpdateAsync();

            if (updated == 0)
            {
                await table.InsertAsync(() => new()
                {
                    GuildId = guildId,
                    Trigger = trigger,
                    Mapping = mapping,
                    DateAdded = DateTime.UtcNow
                });

                // counted after the insert, when the transaction holds the write lock, so concurrent adds can not pass the limit
                var count = await table.CountAsyncLinqToDB(x => x.GuildId == guildId);
                if (count > MAX_ALIASES)
                    return AliasAddResult.LimitReached;
            }

            await tx.CommitAsync();

            var guildDict = _aliases.GetOrAdd(guildId, static _ => new(StringComparer.OrdinalIgnoreCase));
            guildDict[trigger] = mapping;

            return updated == 0 ? AliasAddResult.Added : AliasAddResult.Updated;
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetAliasesAsync(ulong guildId)
    {
        await Task.Yield();
        
        if (_aliases.TryGetValue(guildId, out var aliases))
            return aliases;

        return null;
    }
}