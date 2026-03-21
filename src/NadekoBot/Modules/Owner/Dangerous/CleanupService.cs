using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using LinqToDB.Mapping;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Owner.Dangerous;

/// <summary>
/// Service for cleaning up guild data from the database for guilds the bot is no longer a member of.
/// </summary>
public sealed class CleanupService : ICleanupService, IReadyExecutor, INService
{
    private TypedKey<KeepReport> _cleanupReportKey = new("cleanup:report");
    private TypedKey<bool> _cleanupTriggerKey = new("cleanup:trigger");

    private readonly IPubSub _pubSub;
    private readonly DiscordSocketClient _client;
    private ConcurrentDictionary<int, ulong[]> guildIds = new();
    private readonly IBotCredsProvider _creds;
    private readonly DbService _db;

    public CleanupService(
        IPubSub pubSub,
        DiscordSocketClient client,
        IBotCredsProvider creds,
        DbService db)
    {
        _pubSub = pubSub;
        _client = client;
        _creds = creds;
        _db = db;
    }

    /// <summary>
    /// Initializes the cleanup service subscriptions.
    /// </summary>
    public async Task OnReadyAsync()
    {
        await _pubSub.Sub(_cleanupTriggerKey, OnCleanupTrigger);

        if (_client.ShardId == 0)
            await _pubSub.Sub(_cleanupReportKey, OnKeepReport);
    }

    /// <summary>
    /// Deletes all guild data from the database for guilds the bot is no longer a member of.
    /// </summary>
    /// <returns>Result containing the count of remaining guilds, or null if operation failed.</returns>
    public async Task<KeepResult?> DeleteMissingGuildDataAsync()
    {
        guildIds = new();
        var totalShards = _creds.GetCreds().TotalShards;
        await _pubSub.Pub(_cleanupTriggerKey, true);
        var counter = 0;
        while (guildIds.Keys.Count < totalShards)
        {
            await Task.Delay(1000);
            counter++;

            if (counter >= 5)
                break;
        }

        if (guildIds.Keys.Count < totalShards)
            return default;

        var allIds = guildIds.SelectMany(x => x.Value)
                             .ToArray();

        await using var ctx = _db.GetDbContext();
        await using var linqCtx = ctx.CreateLinqToDBContext();
        await using var tempTable = linqCtx.CreateTempTable<CleanupId>();

        foreach (var chunk in allIds.Chunk(10000))
        {
            await tempTable.BulkCopyAsync(chunk.Select(x => new CleanupId()
            {
                GuildId = x
            }));
        }

        await ctx.GetTable<GuildConfig>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<UserXpStats>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<NadekoExpression>()
                 .Where(x => x.GuildId != null
                             && !tempTable.Select(x => x.GuildId)
                                          .Contains(x.GuildId.Value))
                 .DeleteAsync();

        await ctx.GetTable<Quote>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<PlantedCurrency>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<ImageOnlyChannel>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<ReactionRoleV2>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<DiscordPermOverride>()
                 .Where(x => x.GuildId != null
                             && !tempTable.Select(x => x.GuildId)
                                          .Contains(x.GuildId.Value))
                 .DeleteAsync();

        await ctx.GetTable<Repeater>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<AutoPublishChannel>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<GreetSettings>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<SarGroup>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<Warning>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<WarningPunishment>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<StickyRole>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<AutoTranslateChannel>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<BanTemplate>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        await ctx.GetTable<Reminder>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.ServerId))
                 .DeleteAsync();

        await ctx.GetTable<ButtonRole>()
                 .Where(x => !tempTable.Select(x => x.GuildId)
                                       .Contains(x.GuildId))
                 .DeleteAsync();

        return new()
        {
            GuildCount = guildIds.Keys.Count,
        };
    }

    private ValueTask OnKeepReport(KeepReport report)
    {
        guildIds[report.ShardId] = report.GuildIds;
        return default;
    }

    private ValueTask OnCleanupTrigger(bool arg)
    {
        _pubSub.Pub(_cleanupReportKey,
            new KeepReport()
            {
                ShardId = _client.ShardId,
                GuildIds = _client.GetGuildIds(),
            });

        return default;
    }
}
