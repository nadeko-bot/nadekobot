#nullable disable
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Common;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Searches.Common.StreamNotifications;
using NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

namespace NadekoBot.Modules.Searches.Services;

public sealed class StreamNotificationService : INService, IReadyExecutor
{
    private readonly DbService _db;
    private readonly IBotStrings _strings;
    private readonly NadekoRandom _rng = new NadekoRandom();
    private readonly DiscordSocketClient _client;
    private readonly NotifChecker _streamTracker;

    private readonly Lock _shardLock = new();

    private Dictionary<StreamDataKey, HashSet<ulong>> _trackCounter = new();

    private Dictionary<StreamDataKey, Dictionary<ulong, HashSet<FollowedStream>>> _shardTrackedStreams = new();
    private readonly ConcurrentHashSet<ulong> _offlineNotificationServers = [];
    private readonly ConcurrentHashSet<ulong> _deleteOnOfflineServers = [];

    private readonly IPubSub _pubSub;
    private readonly IMessageSenderService _sender;
    private readonly SearchesConfigService _config;
    private readonly IReplacementService _repSvc;
    private readonly ShardData _shardData;

    public TypedKey<List<StreamData>> StreamsOnlineKey { get; }
    public TypedKey<List<StreamData>> StreamsOfflineKey { get; }

    private readonly TypedKey<FollowStreamPubData> _streamFollowKey;
    private readonly TypedKey<FollowStreamPubData> _streamUnfollowKey;

    public event Func<
        FollowedStream.FType,
        string,
        IReadOnlyCollection<(ulong, ulong)>,
        Task> OnlineMessagesSent = static delegate { return Task.CompletedTask; };

    public StreamNotificationService(
        DbService db,
        DiscordSocketClient client,
        IBotStrings strings,
        IBotCredsProvider creds,
        IHttpClientFactory httpFactory,
        IPubSub pubSub,
        IMessageSenderService sender,
        SearchesConfigService config,
        ShardData shardData,
        IReplacementService repSvc,
        MusicServiceClient music)
    {
        _db = db;
        _client = client;
        _strings = strings;
        _pubSub = pubSub;
        _sender = sender;
        _config = config;
        _repSvc = repSvc;
        _shardData = shardData;

        _streamTracker = new(httpFactory, creds, config, music);

        StreamsOnlineKey = new("streams.online");
        StreamsOfflineKey = new("streams.offline");

        _streamFollowKey = new("stream.follow");
        _streamUnfollowKey = new("stream.unfollow");


        _pubSub.Sub(StreamsOfflineKey, HandleStreamsOffline);
        _pubSub.Sub(StreamsOnlineKey, HandleStreamsOnline);

        if (client.ShardId == 0)
        {
            // only shard 0 will run the tracker,
            // and then publish updates with redis to other shards 
            _streamTracker.OnStreamsOffline += OnStreamsOffline;
            _streamTracker.OnStreamsOnline += OnStreamsOnline;
            _ = _streamTracker.RunAsync();

            _pubSub.Sub(_streamFollowKey, HandleFollowStream);
            _pubSub.Sub(_streamUnfollowKey, HandleUnfollowStream);
        }

        client.JoinedGuild += ClientOnJoinedGuild;
        client.LeftGuild += ClientOnLeftGuild;
    }

    private async Task InitStateAsync()
    {
        await using var uow = _db.GetDbContext();
        var notifyOffline = await uow.GetTable<GuildConfig>()
                                     .Where(gc => gc.NotifyStreamOffline)
                                     .Select(x => x.GuildId)
                                     .ToListAsyncLinqToDB();

        foreach (var guildId in notifyOffline)
            _offlineNotificationServers.Add(guildId);

        var deleteOnOffline = await uow.GetTable<GuildConfig>()
                                       .Where(Queries.GuildOnShard<GuildConfig>(gc => gc.GuildId,
                                           _shardData.TotalShards,
                                           _shardData.ShardId))
                                       .Where(gc => gc.DeleteStreamOnlineMessage)
                                       .Select(x => x.GuildId)
                                       .ToListAsyncLinqToDB()
                                       .Pipe(x => x.ToConcurrentSet());

        foreach (var guildId in deleteOnOffline)
            _deleteOnOfflineServers.Add(guildId);

        var followedStreams = await uow.GetTable<FollowedStream>()
                                       .Where(Queries.GuildOnShard<FollowedStream>(x => x.GuildId,
                                           _shardData.TotalShards,
                                           _shardData.ShardId))
                                       .ToListAsyncLinqToDB();

        // grouped by the key itself, so names which differ only in letter case land in one entry
        _shardTrackedStreams = followedStreams.GroupBy(static x => new StreamDataKey(x.Type, x.Username))
                                              .ToDictionary(static x => x.Key,
                                                  static x => x.GroupBy(static y => y.GuildId)
                                                               .ToDictionary(static y => y.Key,
                                                                   static y => y.AsEnumerable().ToHashSet()));

        // shard 0 will keep track of when there are no more guilds which track a stream
        if (_client.ShardId == 0)
        {
            var allFollowedStreams = uow.Set<FollowedStream>().AsQueryable().ToList();

            foreach (var fs in allFollowedStreams)
                _streamTracker.AddLastData(fs.CreateKey(), null, false);

            _trackCounter = allFollowedStreams.GroupBy(static x => new StreamDataKey(x.Type, x.Username))
                                              .ToDictionary(static x => x.Key,
                                                  static x => x.Select(static fs => fs.GuildId).ToHashSet());
        }
    }

    public async Task OnReadyAsync()
    {
        await InitStateAsync();
        
        if (_client.ShardId != 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var errorLimit = TimeSpan.FromHours(12);
                var failingStreams = _streamTracker.GetFailingStreams(errorLimit, true).ToList();

                if (!failingStreams.Any())
                    continue;

                var deleteGroups = failingStreams.GroupBy(x => x.Type)
                                                 .ToDictionary(x => x.Key, x => x.Select(y => y.Name).ToList());

                await using var uow = _db.GetDbContext();
                foreach (var kvp in deleteGroups)
                {
                    Log.Information(
                        "Deleting {StreamCount} {Platform} streams because they've been erroring for more than {ErrorLimit}: {RemovedList}",
                        kvp.Value.Count,
                        kvp.Key,
                        errorLimit,
                        string.Join(", ", kvp.Value));

                    // stream keys match in any letter case, so the delete does too
                    var names = kvp.Value.Select(static x => x.ToLowerInvariant()).ToList();
                    var toDelete = uow.Set<FollowedStream>()
                                      .AsQueryable()
                                      .Where(x => x.Type == kvp.Key && names.Contains(x.Username.ToLower()))
                                      .ToList();

                    uow.RemoveRange(toDelete);
                    await uow.SaveChangesAsync();

                    foreach (var loginToDelete in kvp.Value)
                        _streamTracker.UntrackStreamByKey(new(kvp.Key, loginToDelete));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error cleaning up FollowedStreams");
            }
        }
    }

    /// <summary>
    ///     Handles follow stream pubs to keep the counter up to date.
    ///     When counter reaches 0, stream is removed from tracking because
    ///     that means no guilds are subscribed to that stream anymore
    /// </summary>
    private ValueTask HandleFollowStream(FollowStreamPubData info)
    {
        _streamTracker.AddLastData(info.Key, null, false);
        lock (_shardLock)
        {
            var key = info.Key;
            if (_trackCounter.ContainsKey(key))
            {
                _trackCounter[key].Add(info.GuildId);
            }
            else
            {
                _trackCounter[key] = [info.GuildId];
            }
        }

        return default;
    }

    /// <summary>
    ///     Handles unfollow pubs to keep the counter up to date.
    ///     When counter reaches 0, stream is removed from tracking because
    ///     that means no guilds are subscribed to that stream anymore
    /// </summary>
    private ValueTask HandleUnfollowStream(FollowStreamPubData info)
    {
        lock (_shardLock)
        {
            var key = info.Key;
            if (!_trackCounter.TryGetValue(key, out var set))
            {
                // it should've been removed already?
                _streamTracker.UntrackStreamByKey(in key);
                return default;
            }

            set.Remove(info.GuildId);
            if (set.Count != 0)
                return default;

            _trackCounter.Remove(key);
            // if no other guilds are following this stream
            // untrack the stream
            _streamTracker.UntrackStreamByKey(in key);
        }

        return default;
    }

    private async ValueTask HandleStreamsOffline(List<StreamData> offlineStreams)
    {
        foreach (var stream in offlineStreams)
        {
            var key = stream.CreateKey();
            var targets = GetTargets(key);

            foreach (var fs in targets)
            {
                // send offline stream notifications only to guilds which enable it with .stoff
                if (!_offlineNotificationServers.Contains(fs.GuildId))
                    continue;

                try
                {
                    var ch = _client.GetGuild(fs.GuildId)
                                    ?.GetTextChannel(fs.ChannelId);

                    if (ch is null || !FeedsService.CanPostIn(ch.Guild.CurrentUser, ch))
                        continue;

                    await _sender.Response(ch).Embed(GetEmbed(fs.GuildId, stream)).SendAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex,
                        "Unable to post the offline notification of {Stream} in guild {GuildId}",
                        key.Name,
                        fs.GuildId);
                }
            }
        }
    }


    private async ValueTask HandleStreamsOnline(List<StreamData> onlineStreams)
    {
        foreach (var stream in onlineStreams)
        {
            var key = stream.CreateKey();
            var targets = GetTargets(key);
            if (targets.Length == 0)
                continue;

            var pairs = new List<(ulong, ulong)>(targets.Length);

            foreach (var fs in targets)
            {
                try
                {
                    var textChannel = _client.GetGuild(fs.GuildId)
                                             ?.GetTextChannel(fs.ChannelId);

                    if (textChannel is null
                        || !FeedsService.CanPostIn(textChannel.Guild.CurrentUser, textChannel))
                        continue;

                    var repCtx = new ReplacementContext(guild: textChannel.Guild, client: _client)
                        .WithOverride("%platform%", () => fs.Type.ToString());

                    var message = string.IsNullOrWhiteSpace(fs.Message)
                        ? ""
                        : await _repSvc.ReplaceAsync(fs.Message, repCtx);

                    var msg = await _sender.Response(textChannel)
                                           .Embed(GetEmbed(fs.GuildId, stream, false))
                                           .Text(message)
                                           .Mentions(TemplateMentions.FromTemplate(fs.Message))
                                           .SendAsync();

                    // only cache the ids of channel/message pairs 
                    if (_deleteOnOfflineServers.Contains(fs.GuildId))
                        pairs.Add((textChannel.Id, msg.Id));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex,
                        "Unable to post the online notification of {Stream} in guild {GuildId}",
                        key.Name,
                        fs.GuildId);
                }
            }

            // push online stream messages to redis
            // when streams go offline, any server which
            // has the online stream message deletion feature
            // enabled will have the online messages deleted
            try
            {
                if (pairs.Count > 0)
                    await OnlineMessagesSent(key.Type, key.Name, pairs);
            }
            catch
            {
            }
        }
    }

    private FollowedStream[] GetTargets(in StreamDataKey key)
    {
        lock (_shardLock)
        {
            if (!_shardTrackedStreams.TryGetValue(key, out var fss))
                return [];

            var count = 0;
            foreach (var set in fss.Values)
                count += set.Count;

            var targets = new FollowedStream[count];
            var i = 0;
            foreach (var set in fss.Values)
            {
                set.CopyTo(targets, i);
                i += set.Count;
            }

            return targets;
        }
    }

    private Task OnStreamsOnline(List<StreamData> data)
        => _pubSub.Pub(StreamsOnlineKey, data);

    private Task OnStreamsOffline(List<StreamData> data)
        => _pubSub.Pub(StreamsOfflineKey, data);

    private async Task ClientOnJoinedGuild(SocketGuild guild)
    {
        await using var uow = _db.GetDbContext();
        var fs = await uow.Set<FollowedStream>()
                          .Where(x => x.GuildId == guild.Id)
                          .ToListAsyncLinqToDB();

        var notifyOffline = await uow.GetTable<GuildConfig>()
                                     .Where(x => x.GuildId == guild.Id)
                                     .Select(x => x.NotifyStreamOffline)
                                     .FirstOrDefaultAsyncLinqToDB();

        if (notifyOffline)
            _offlineNotificationServers.Add(guild.Id);

        lock (_shardLock)
        {
            foreach (var followedStream in fs)
                GetLocalGuildStreams(followedStream.CreateKey(), guild.Id).Add(followedStream);
        }

        foreach (var followedStream in fs)
            PublishFollowStream(followedStream);
    }

    private async Task ClientOnLeftGuild(SocketGuild guild)
    {
        await using var uow = _db.GetDbContext();
        var followedStreams = await uow.Set<FollowedStream>()
                                       .Where(x => x.GuildId == guild.Id)
                                       .ToListAsyncLinqToDB();

        _offlineNotificationServers.TryRemove(guild.Id);

        RemoveLocal(guild.Id, followedStreams);

        foreach (var followedStream in followedStreams)
            await PublishUnfollowStream(followedStream);
    }

    private void RemoveLocal(ulong guildId, IEnumerable<FollowedStream> followedStreams)
    {
        lock (_shardLock)
        {
            foreach (var fs in followedStreams)
                GetLocalGuildStreams(fs.CreateKey(), guildId).Remove(fs);
        }
    }

    public async Task<int> ClearAllStreams(ulong guildId)
    {
        await using var uow = _db.GetDbContext();

        var followedStreams = await uow.GetTable<FollowedStream>()
                                       .Where(x => x.GuildId == guildId)
                                       .DeleteWithOutputAsync();

        RemoveLocal(guildId, followedStreams);

        foreach (var fs in followedStreams)
            await PublishUnfollowStream(fs);

        return followedStreams.Length;
    }

    public async Task<FollowedStream> UnfollowStreamByIdAsync(ulong guildId, int id)
    {
        await using var uow = _db.GetDbContext();
        var deleted = await uow.GetTable<FollowedStream>()
                               .Where(x => x.Id == id && x.GuildId == guildId)
                               .DeleteWithOutputAsync();

        if (deleted.Length == 0)
            return null;

        await AfterUnfollowInternalAsync(uow, deleted[0]);
        return deleted[0];
    }

    private async Task AfterUnfollowInternalAsync(DbContext uow, FollowedStream fs)
    {
        // shard 0 counts followers per server, and the local map holds one entry per channel,
        // so another row of the same stream must keep them; read after the delete so parallel removes all see it
        var guildId = fs.GuildId;
        var type = fs.Type;
        var username = fs.Username;
        var channels = await uow.GetTable<FollowedStream>()
                                .Where(x => x.GuildId == guildId && x.Type == type && x.Username.ToLower() == username.ToLower())
                                .Select(x => x.ChannelId)
                                .ToListAsyncLinqToDB();

        if (!channels.Contains(fs.ChannelId))
            RemoveLocal(guildId, [fs]);

        if (channels.Count == 0)
            await PublishUnfollowStream(fs);
    }

    private void PublishFollowStream(FollowedStream fs)
        => _pubSub.Pub(_streamFollowKey,
            new()
            {
                Key = fs.CreateKey(),
                GuildId = fs.GuildId
            });

    private Task PublishUnfollowStream(FollowedStream fs)
        => _pubSub.Pub(_streamUnfollowKey,
            new()
            {
                Key = fs.CreateKey(),
                GuildId = fs.GuildId
            });

    public readonly record struct FollowStreamOutcome(
        FollowStreamResult Result,
        StreamData Data,
        FollowedStream Stream,
        FollowedStream.FType? Platform);

    public async Task<FollowStreamOutcome> FollowStream(
        ulong guildId,
        ulong channelId,
        string url,
        string message = null)
    {
        var lookup = await _streamTracker.LookupByUrlAsync(url);
        switch (lookup.Status)
        {
            case StreamLookupStatus.Unsupported:
                return new(FollowStreamResult.Unsupported, null, null, null);
            case StreamLookupStatus.Unavailable:
                return new(FollowStreamResult.Unavailable, null, null, lookup.Platform);
        }

        if (lookup.Data is not { } data)
            return new(FollowStreamResult.NotFound, null, null, lookup.Platform);

        var fs = new FollowedStream
        {
            Type = data.StreamType,
            Username = data.UniqueName,
            PrettyName = data.Name,
            ChannelId = channelId,
            GuildId = guildId,
            Message = string.IsNullOrEmpty(message) ? null : message
        };

        await using (var uow = _db.GetDbContext())
        {
            var result = await TryAddFollowAsync(uow, fs, _config.Data.FollowedStreams.MaxCount);
            if (result != FollowStreamResult.Followed)
                return new(result, result == FollowStreamResult.AlreadyFollowed ? data : null, null, data.StreamType);
        }

        // this shard posts the alerts for this channel when shard 0 publishes a status change of the stream
        lock (_shardLock)
        {
            GetLocalGuildStreams(fs.CreateKey(), guildId).Add(fs);
        }

        PublishFollowStream(fs);

        return new(FollowStreamResult.Followed, data, fs, data.StreamType);
    }

    public static async Task<FollowStreamResult> TryAddFollowAsync(DbContext ctx, FollowedStream fs, int maxCount)
    {
        var guildId = fs.GuildId;
        var channelId = fs.ChannelId;
        var type = fs.Type;
        var name = fs.Username;
        var prettyName = fs.PrettyName;
        var message = fs.Message;
        // the duplicate check, the server limit and the insert are one statement, so parallel adds cannot pass them
        await using var lctx = ctx.CreateLinqToDBContext();
        var table = lctx.GetTable<FollowedStream>();
        var ids = await lctx.SelectQuery(() => 1)
            .Where(_ => !table.Any(x => x.GuildId == guildId
                                        && x.ChannelId == channelId
                                        && x.Type == type
                                        && x.Username.ToLower() == name.ToLower())
                        && (maxCount == -1 || table.Count(x => x.GuildId == guildId) < maxCount))
            .InsertWithOutputAsync(table,
                _ => new FollowedStream
                {
                    GuildId = guildId,
                    ChannelId = channelId,
                    Type = type,
                    Username = name,
                    PrettyName = prettyName,
                    Message = message,
                },
                x => x.Id);

        if (ids.Length > 0)
        {
            fs.Id = ids[0];
            return FollowStreamResult.Followed;
        }

        var duplicate = await table.AnyAsyncLinqToDB(x => x.GuildId == guildId
                                                          && x.ChannelId == channelId
                                                          && x.Type == type
                                                          && x.Username.ToLower() == name.ToLower());
        return duplicate ? FollowStreamResult.AlreadyFollowed : FollowStreamResult.LimitReached;
    }

    public EmbedBuilder GetEmbed(ulong guildId, StreamData status, bool showViewers = true)
    {
        var embed = _sender.CreateEmbed(guildId)
                           .WithTitle(status.Name)
                           .WithUrl(status.StreamUrl)
                           .WithDescription(status.StreamUrl)
                           .AddField(GetText(guildId, strs.status), status.IsLive ? "🟢 Online" : "🔴 Offline", true);

        if (showViewers)
        {
            embed.AddField(GetText(guildId, strs.viewers),
                status.Viewers == 0 && !status.IsLive
                    ? "-"
                    : status.Viewers,
                true);
        }

        if (status.IsLive)
            embed = embed.WithOkColor();
        else
            embed = embed.WithErrorColor();

        if (!string.IsNullOrWhiteSpace(status.Title))
            embed.WithAuthor(status.Title);

        if (!string.IsNullOrWhiteSpace(status.Game))
            embed.AddField(GetText(guildId, strs.streaming), status.Game, true);

        if (!string.IsNullOrWhiteSpace(status.AvatarUrl))
            embed.WithThumbnailUrl(status.AvatarUrl);

        if (!string.IsNullOrWhiteSpace(status.Preview))
            embed.WithImageUrl(status.Preview + "?dv=" + _rng.Next());

        return embed;
    }

    private string GetText(ulong guildId, LocStr str)
        => _strings.GetText(str, guildId);

    public async Task<bool> ToggleStreamOffline(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        await uow.EnsureGuildConfigAsync(guildId);
        var newValue = (await uow.GetTable<GuildConfig>()
                                 .Where(x => x.GuildId == guildId)
                                 .Set(x => x.NotifyStreamOffline, x => !x.NotifyStreamOffline)
                                 .UpdateWithOutputAsync((_, inserted) => inserted.NotifyStreamOffline))
                       .FirstOrDefault();

        if (newValue)
            _offlineNotificationServers.Add(guildId);
        else
            _offlineNotificationServers.TryRemove(guildId);

        return newValue;
    }

    public async Task<bool> ToggleStreamOnlineDelete(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        await uow.EnsureGuildConfigAsync(guildId);
        var newValue = (await uow.GetTable<GuildConfig>()
                                 .Where(x => x.GuildId == guildId)
                                 .Set(x => x.DeleteStreamOnlineMessage, x => !x.DeleteStreamOnlineMessage)
                                 .UpdateWithOutputAsync((_, inserted) => inserted.DeleteStreamOnlineMessage))
                       .FirstOrDefault();

        if (newValue)
            _deleteOnOfflineServers.Add(guildId);
        else
            _deleteOnOfflineServers.TryRemove(guildId);

        return newValue;
    }

    public Task<StreamData> GetStreamDataAsync(string url)
        => _streamTracker.GetStreamDataByUrlAsync(url);

    private HashSet<FollowedStream> GetLocalGuildStreams(in StreamDataKey key, ulong guildId)
    {
        if (_shardTrackedStreams.TryGetValue(key, out var map))
        {
            if (map.TryGetValue(guildId, out var set))
                return set;
            return map[guildId] = [];
        }

        _shardTrackedStreams[key] = new()
        {
            { guildId, [] }
        };
        return _shardTrackedStreams[key][guildId];
    }

    public bool SetStreamMessage(
        ulong guildId,
        int id,
        string message,
        out FollowedStream fs)
    {
        using var uow = _db.GetDbContext();
        fs = uow.Set<FollowedStream>()
                .FirstOrDefault(x => x.GuildId == guildId && x.Id == id);

        if (fs is null)
            return false;

        fs.Message = message;
        lock (_shardLock)
        {
            var streams = GetLocalGuildStreams(fs.CreateKey(), guildId);

            // message doesn't participate in equality checking
            // removing and adding = update
            streams.Remove(fs);
            streams.Add(fs);
        }

        uow.SaveChanges();

        return true;
    }

    public int SetStreamMessageForAll(ulong guildId, string message)
    {
        using var uow = _db.GetDbContext();

        var all = uow.Set<FollowedStream>()
                     .Where(x => x.GuildId == guildId)
                     .ToList();

        if (all.Count == 0)
            return 0;

        all.ForEach(x => x.Message = message);

        uow.SaveChanges();

        lock (_shardLock)
        {
            foreach (var fs in all)
            {
                var streams = GetLocalGuildStreams(fs.CreateKey(), guildId);

                // message doesn't participate in equality checking
                // removing and adding = update
                streams.Remove(fs);
                streams.Add(fs);
            }
        }

        return all.Count;
    }

    public int MaxFollowedStreams
        => _config.Data.FollowedStreams.MaxCount;

    public async Task<List<FollowedStream>> GetStreamsAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        return await uow.GetTable<FollowedStream>()
                        .Where(x => x.GuildId == guildId)
                        .OrderBy(x => x.Id)
                        .ToListAsyncLinqToDB();
    }

    public async Task<(bool NotifyOffline, bool DeleteOnlineMessage)> GetStreamSettingsAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var row = await uow.GetTable<GuildConfig>()
                           .Where(x => x.GuildId == guildId)
                           .Select(x => new
                           {
                               x.NotifyStreamOffline,
                               x.DeleteStreamOnlineMessage
                           })
                           .FirstOrDefaultAsyncLinqToDB();

        return row is null ? (false, false) : (row.NotifyStreamOffline, row.DeleteStreamOnlineMessage);
    }

    // Sets each given value, unlike .stoff and .stondel, which toggle.
    public async Task<(bool NotifyOffline, bool DeleteOnlineMessage)> SetStreamSettingsAsync(
        ulong guildId,
        bool? notifyOffline,
        bool? deleteOnlineMessage)
    {
        if (notifyOffline is null && deleteOnlineMessage is null)
            return await GetStreamSettingsAsync(guildId);

        await using var uow = _db.GetDbContext();
        await uow.EnsureGuildConfigAsync(guildId);

        var update = uow.GetTable<GuildConfig>().Where(x => x.GuildId == guildId).AsUpdatable();
        if (notifyOffline is { } offline)
            update = update.Set(x => x.NotifyStreamOffline, offline);
        if (deleteOnlineMessage is { } delete)
            update = update.Set(x => x.DeleteStreamOnlineMessage, delete);

        var rows = await update.UpdateWithOutputAsync((_, inserted) => new
        {
            inserted.NotifyStreamOffline,
            inserted.DeleteStreamOnlineMessage
        });

        var (newOffline, newDelete) = rows.Length > 0
            ? (rows[0].NotifyStreamOffline, rows[0].DeleteStreamOnlineMessage)
            : (false, false);

        if (newOffline)
            _offlineNotificationServers.Add(guildId);
        else
            _offlineNotificationServers.TryRemove(guildId);

        if (newDelete)
            _deleteOnOfflineServers.Add(guildId);
        else
            _deleteOnOfflineServers.TryRemove(guildId);

        return (newOffline, newDelete);
    }

    public enum UpdateStreamResult
    {
        Updated,
        NotFound,
        Duplicate
    }

    // A parallel channel change of the same row makes the conditional update miss; then it reads the row again.
    private const int MAX_UPDATE_ATTEMPTS = 3;

    // Changes only the given fields. A new channel must not already follow the same stream.
    public async Task<(UpdateStreamResult Result, FollowedStream Stream)> UpdateStreamAsync(
        ulong guildId,
        int id,
        ulong? channelId,
        bool setMessage,
        string message)
    {
        await using var uow = _db.GetDbContext();
        var table = uow.GetTable<FollowedStream>();
        var newMessage = string.IsNullOrEmpty(message) ? null : message;

        for (var attempt = 0; attempt < MAX_UPDATE_ATTEMPTS; attempt++)
        {
            // A projection, because an entity which this context tracks would replace the returned row.
            var old = await table.Where(x => x.Id == id && x.GuildId == guildId)
                                 .Select(x => new
                                 {
                                     x.ChannelId,
                                     x.Type,
                                     x.Username
                                 })
                                 .FirstOrDefaultAsyncLinqToDB();
            if (old is null)
                return (UpdateStreamResult.NotFound, null);

            var oldChannel = old.ChannelId;
            var newChannel = channelId ?? oldChannel;
            var type = old.Type;
            var name = old.Username;

            var update = table
                         .Where(x => x.Id == id
                                     && x.GuildId == guildId
                                     && x.ChannelId == oldChannel
                                     && !table.Any(y => y.Id != id
                                                        && y.GuildId == guildId
                                                        && y.ChannelId == newChannel
                                                        && y.Type == type
                                                        && y.Username.ToLower() == name.ToLower()))
                         .AsUpdatable();

            if (channelId is not null)
                update = update.Set(x => x.ChannelId, newChannel);
            if (setMessage)
                update = update.Set(x => x.Message, newMessage);
            if (channelId is null && !setMessage)
                update = update.Set(x => x.ChannelId, oldChannel);

            var rows = await update.UpdateWithOutputAsync(static (_, inserted) => inserted);
            if (rows.Length == 0)
            {
                var current = await table.Where(x => x.Id == id && x.GuildId == guildId)
                                         .Select(x => (ulong?)x.ChannelId)
                                         .FirstOrDefaultAsyncLinqToDB();
                if (current is null)
                    return (UpdateStreamResult.NotFound, null);

                if (current != oldChannel)
                    continue;

                return (UpdateStreamResult.Duplicate, null);
            }

            var updated = rows[0];

            // the local set compares the channel, so the old entry goes out before the new one comes in
            lock (_shardLock)
            {
                var streams = GetLocalGuildStreams(updated.CreateKey(), guildId);
                streams.Remove(new FollowedStream
                {
                    ChannelId = oldChannel,
                    Type = type,
                    Username = name
                });
                streams.Add(updated);
            }

            return (UpdateStreamResult.Updated, updated);
        }

        return (UpdateStreamResult.Duplicate, null);
    }

    public enum FollowStreamResult
    {
        Followed,
        NotFound,
        AlreadyFollowed,
        LimitReached,
        Unsupported,
        Unavailable
    }

    public sealed class FollowStreamPubData
    {
        public StreamDataKey Key { get; init; }
        public ulong GuildId { get; init; }
    }

    public async Task<List<FollowedStream>> GetAllStreamsAsync(SocketGuild guild)
    {
        await using var uow = _db.GetDbContext();
        var all = await uow.Set<FollowedStream>()
                           .Where(x => x.GuildId == guild.Id)
                           .OrderBy(x => x.Id)
                           .ToListAsyncEF();

        var allStreams = new List<FollowedStream>();
        for (var index = all.Count - 1; index >= 0; index--)
        {
            var fs = all[index];
            if (guild.GetTextChannel(fs.ChannelId) is null)
                await UnfollowStreamByIdAsync(fs.GuildId, fs.Id);
            else
                allStreams.Insert(0, fs);
        }

        return allStreams;
    }
}
