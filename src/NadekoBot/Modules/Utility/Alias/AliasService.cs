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

    private const string TARGET_PLACEHOLDER = "%target%";

    private readonly DbService _db;
    private readonly IMessageSenderService _sender;
    private readonly ShardData _shardData;
    private readonly CommandService _cmds;
    private readonly ICommandHandler _cmdHandler;
    private readonly ConcurrentDictionary<(ulong GuildId, string Mapping), bool> _loggedBlocked = new();

    public AliasService(
        DbService db,
        IMessageSenderService sender,
        ShardData shardData,
        CommandService cmds,
        ICommandHandler cmdHandler)
    {
        _sender = sender;
        _shardData = shardData;
        _db = db;
        _cmds = cmds;
        _cmdHandler = cmdHandler;
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
            var newInput = FindMapping(maps, input, out var mapping);

            if (newInput is not null && mapping is not null)
            {
                // Aliases saved before this check existed still exist, so the check also runs here.
                if (GivesArgsToSpendingCommand(guild, mapping))
                {
                    if (_loggedBlocked.TryAdd((guild.Id, mapping), true))
                        Log.Warning("Alias {Mapping} in guild {GuildId} gives fixed arguments to a command which spends currency. It does not run",
                            mapping,
                            guild.Id);

                    return null;
                }

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
        => FindMapping(maps, input, out _);

    private static string? FindMapping(ConcurrentDictionary<string, string> maps, string input, out string? usedMapping)
    {
        usedMapping = null;
        if (maps.TryGetValue(input, out var exact))
        {
            usedMapping = exact;
            return exact;
        }

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

        usedMapping = bestMapping;
        var rest = input.AsSpan(bestTrigger.Length);
        if (bestMapping.Contains(TARGET_PLACEHOLDER, StringComparison.Ordinal))
            return bestMapping.Replace(TARGET_PLACEHOLDER, rest.ToString(), StringComparison.Ordinal);

        return string.Concat(bestMapping, " ", rest);
    }

    public bool GivesArgsToSpendingCommand(IGuild guild, string mapping)
        => GivesArgsToSpendingCommand(_cmds, _cmdHandler.GetPrefix(guild), mapping);

    // An admin who fixes the amount or the receiver could make each member who uses the alias pay or give away currency.
    public static bool GivesArgsToSpendingCommand(CommandService cmds, string prefix, string mapping)
    {
        var text = mapping.AsSpan().Trim();
        if (!text.StartsWith(prefix, StringComparison.InvariantCulture))
            return false;

        var body = text[prefix.Length..].TrimStart().ToString();
        var result = cmds.Search(body);
        if (!result.IsSuccess)
            return false;

        foreach (var match in result.Commands)
        {
            if (!SpendsCurrency(match.Command))
                continue;

            var args = match.Alias.Length < body.Length
                ? body.AsSpan(match.Alias.Length)
                : ReadOnlySpan<char>.Empty;

            if (HasFixedArgs(args))
                return true;
        }

        return false;
    }

    private static bool SpendsCurrency(CommandInfo cmd)
    {
        foreach (var attr in cmd.Attributes)
        {
            if (attr is SpendsCurrencyAttribute)
                return true;
        }

        return false;
    }

    private static bool HasFixedArgs(ReadOnlySpan<char> args)
    {
        while (true)
        {
            args = args.TrimStart();
            if (args.IsEmpty)
                return false;

            if (!args.StartsWith(TARGET_PLACEHOLDER, StringComparison.Ordinal))
                return true;

            args = args[TARGET_PLACEHOLDER.Length..];
        }
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