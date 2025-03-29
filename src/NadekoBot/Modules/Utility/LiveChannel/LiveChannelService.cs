using System.Net;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Patronage;
using Newtonsoft.Json.Linq;

namespace NadekoBot.Modules.Utility.LiveChannel;

/// <summary>
/// Service for managing live channels.
/// </summary>
public class LiveChannelService(
    DbService db,
    DiscordSocketClient client,
    IReplacementService repSvc,
    IPatronageService patron,
    ShardData shardData) : IReadyExecutor, INService
{
    public const int DEFAULT_MAX_LIVECHANNELS = 5;

    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, LiveChannelConfig>> _liveChannels = new();

    /// <summary>
    /// Initializes data when bot is ready
    /// </summary>
    public async Task OnReadyAsync()
    {
        // Load all existing live channels into memory
        await using var uow = db.GetDbContext();
        var configs = await uow.GetTable<LiveChannelConfig>()
            .AsNoTracking()
            .Where(x => Queries.GuildOnShard(x.GuildId, shardData.TotalShards, shardData.ShardId))
            .ToListAsyncLinqToDB();

        foreach (var config in configs)
        {
            var guildDict = _liveChannels.GetOrAdd(
                config.GuildId,
                _ => new());

            guildDict[config.ChannelId] = config;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                // get all live channels from cache
                var channels = new List<LiveChannelConfig>(_liveChannels.Count * 2);

                foreach (var (_, vals) in _liveChannels)
                {
                    foreach (var (_, config) in vals)
                    {
                        channels.Add(config);
                    }
                }

                foreach (var config in channels)
                {
                    var guild = client.GetGuild(config.GuildId);
                    var channel = guild?.GetChannel(config.ChannelId);

                    if (channel is null)
                    {
                        await RemoveLiveChannelAsync(config.GuildId, config.ChannelId);
                        continue;
                    }

                    var repCtx = new ReplacementContext(
                        user: null,
                        guild: guild,
                        client: client
                    );

                    try
                    {
                        var text = await repSvc.ReplaceAsync(config.Template, repCtx);

                        // only update if needed
                        if (channel.Name != text)
                            await channel.ModifyAsync(x => x.Name = text);
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden
                                                   || ex.DiscordCode == DiscordErrorCode.MissingPermissions
                                                   || ex.HttpCode == HttpStatusCode.NotFound)
                    {
                        await RemoveLiveChannelAsync(config.GuildId, config.ChannelId);
                        Log.Warning(
                            "Channel {ChannelId} in guild {GuildId} is not accessible. Live channel will be removed",
                            config.ChannelId,
                            config.GuildId);
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.TooManyRequests ||
                                                   ex.DiscordCode == DiscordErrorCode.ChannelWriteRatelimit)
                    {
                        Log.Warning(ex, "LiveChannel hit a ratelimit. Sleeping for 2 minutes: {Message}", ex.Message);
                        await Task.Delay(2.Minutes());
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Error in live channel service: {ErrorMessage}", e.Message);
                    }

                    // wait for half a second to reduce the chance of global ratelimits
                    await Task.Delay(500);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in live channel service: {ErrorMessage}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Adds a new live channel configuration to the specified guild.
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="channelId">ID of the channel</param>
    /// <param name="template">Template text to use for the channel</param>
    /// <returns>True if successfully added, false otherwise</returns>
    public async Task<bool> AddLiveChannelAsync(ulong guildId, ulong channelId, ulong guildOwnerId, string template)
    {
        var guildDict = _liveChannels.GetOrAdd(
            guildId,
            _ => new());

        if (!guildDict.ContainsKey(channelId) && guildDict.Count >= await GetMaxLiveChannels(guildOwnerId))
            return false;

        await using var uow = db.GetDbContext();
        await uow.GetTable<LiveChannelConfig>()
            .InsertOrUpdateAsync(() => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    Template = template
                },
                (_) => new()
                {
                    Template = template
                },
                () => new()
                {
                    GuildId = guildId,
                    ChannelId = channelId
                });

        // Add to in-memory cache
        var newConfig = new LiveChannelConfig
        {
            GuildId = guildId,
            ChannelId = channelId,
            Template = template
        };

        guildDict[channelId] = newConfig;
        return true;
    }

    /// <summary>
    /// Removes a live channel configuration from the specified guild.
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="channelId">ID of the channel to remove as live</param>
    /// <returns>True if successfully removed, false otherwise</returns>
    public async Task<bool> RemoveLiveChannelAsync(ulong guildId, ulong channelId)
    {
        if (!_liveChannels.TryGetValue(guildId, out var guildDict) ||
            !guildDict.TryRemove(channelId, out _))
            return false;

        await using var uow = db.GetDbContext();
        await uow.GetTable<LiveChannelConfig>()
            .Where(x => x.GuildId == guildId && x.ChannelId == channelId)
            .DeleteAsync();

        return true;
    }

    /// <summary>
    /// Gets all live channels for a guild.
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <returns>List of live channel configurations</returns>
    public async Task<List<LiveChannelConfig>> GetLiveChannelsAsync(ulong guildId)
    {
        await using var uow = db.GetDbContext();
        return await uow.GetTable<LiveChannelConfig>()
            .AsNoTracking()
            .Where(x => x.GuildId == guildId)
            .ToListAsyncLinqToDB();
    }


    public async Task<int> GetMaxLiveChannels(ulong guildOwnerId)
    {
        var limit = await patron.GetUserLimit("livechannels", guildOwnerId, DEFAULT_MAX_LIVECHANNELS);
        return limit;
    }
}