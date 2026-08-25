using System.Collections.Frozen;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.AiAgent;

// Lets a selfhoster grant agent access without the patronage system.
public sealed class AiAgentWhitelistService(DbService db, IPubSub pubSub) : INService, IReadyExecutor
{
    private static readonly TypedKey<bool> _reloadKey = new("aiagent.whitelist.reload");

    private static readonly FrozenSet<ulong> _empty = FrozenSet<ulong>.Empty;

    private FrozenDictionary<AiAgentWhitelistType, FrozenSet<ulong>> _entries =
        FrozenDictionary<AiAgentWhitelistType, FrozenSet<ulong>>.Empty;

    public async Task OnReadyAsync()
    {
        await pubSub.Sub(_reloadKey, async _ => await ReloadInternalAsync());
        await ReloadInternalAsync();
    }

    public bool IsWhitelisted(AiAgentWhitelistType type, ulong id)
        => GetSet(type).Contains(id);

    public bool IsAnyWhitelisted(AiAgentWhitelistType type, IReadOnlyCollection<ulong> ids)
    {
        var set = GetSet(type);
        if (set.Count == 0 || ids.Count == 0)
            return false;

        foreach (var id in ids)
        {
            if (set.Contains(id))
                return true;
        }

        return false;
    }

    public FrozenSet<ulong> GetSet(AiAgentWhitelistType type)
        => _entries.TryGetValue(type, out var set) ? set : _empty;

    // Returns true when the entry is whitelisted now.
    public async Task<bool> ToggleAsync(AiAgentWhitelistType type, ulong id)
    {
        await using var ctx = db.GetDbContext();

        var deleted = await ctx.GetTable<AiAgentWhitelistEntry>()
            .Where(x => x.Type == type && x.ItemId == id)
            .DeleteAsync();

        if (deleted == 0)
        {
            await ctx.GetTable<AiAgentWhitelistEntry>()
                .InsertAsync(() => new()
                {
                    Type = type,
                    ItemId = id
                });
        }

        await ReloadInternalAsync();
        await pubSub.Pub(_reloadKey, true);

        return deleted == 0;
    }

    private async Task ReloadInternalAsync()
    {
        await using var ctx = db.GetDbContext();
        var entries = await ctx.GetTable<AiAgentWhitelistEntry>()
            .ToListAsyncLinqToDB();

        var grouped = new Dictionary<AiAgentWhitelistType, HashSet<ulong>>();
        foreach (var entry in entries)
        {
            if (!grouped.TryGetValue(entry.Type, out var set))
            {
                set = [];
                grouped[entry.Type] = set;
            }

            set.Add(entry.ItemId);
        }

        var frozen = new Dictionary<AiAgentWhitelistType, FrozenSet<ulong>>(grouped.Count);
        foreach (var (type, set) in grouped)
            frozen[type] = set.ToFrozenSet();

        _entries = frozen.ToFrozenDictionary();
    }
}
