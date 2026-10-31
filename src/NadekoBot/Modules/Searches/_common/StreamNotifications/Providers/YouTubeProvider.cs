using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Searches.Services;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

// Tracks YouTube livestreams through Invidious when searches.yml lists an instance, else through the music service.
public sealed partial class YouTubeProvider : Provider
{
    private const string LIVE_GAME = "Livestream";
    private const string STATUS_LIVE = "live";
    private const string STATUS_OFFLINE = "offline";
    private const string STATUS_MISSING = "missing";
    private const string WATCH_URL = "https://youtube.com/watch?v=";
    private const string CHANNEL_URL = "https://www.youtube.com/channel/";

    private readonly IHttpClientFactory _httpFactory;
    private readonly SearchesConfigService _scs;
    private readonly MusicServiceClient _music;
    private readonly NadekoRandom _rng = new();
    private readonly ConcurrentDictionary<string, byte> _loggedNonIds = new();

    /// <summary>
    /// Regex to match YouTube handles
    /// </summary>
    /// <returns>Regex</returns>
    [GeneratedRegex(@"youtu(?:\.be|be\.com)\/@(?<handle>[^\/\?#]+)")]
    private static partial Regex HandleRegex();

    // channel id regex
    [GeneratedRegex(@"youtu(?:\.be|be\.com)\/channel\/(?<channelid>[^\/\?#]+)")]
    private static partial Regex ChannelIdRegex();

    /// <summary>
    /// Type of the platform.
    /// </summary>
    public override FollowedStream.FType Platform
        => FollowedStream.FType.Youtube;

    public YouTubeProvider(
        IHttpClientFactory httpFactory,
        SearchesConfigService scs,
        MusicServiceClient music)
    {
        _httpFactory = httpFactory;
        _scs = scs;
        _music = music;
    }

    private bool UseInvidious
        => _scs.Data.InvidiousInstances is { Count: > 0 };

    /// <summary>
    /// Checks whether the specified url is a valid YouTube url.
    /// </summary>
    /// <param name="url">Url to check</param>
    /// <returns>True if valid, otherwise false</returns>
    public override Task<bool> IsValidUrl(string url)
    {
        var success = HandleRegex().IsMatch(url)
                      || ChannelIdRegex().IsMatch(url)
                      || FeedsService.IsYtChannelLink(url);

        return Task.FromResult(success);
    }

    public override async Task<StreamLookup> LookupByUrlAsync(string url)
    {
        if (UseInvidious)
        {
            var data = await GetInvidiousDataByUrlInternalAsync(url);
            return data is null ? StreamLookup.NotFound : StreamLookup.Found(data);
        }

        if (!_music.IsConfigured)
            return StreamLookup.Unavailable;

        var resolved = await _music.ResolveChannelAsync(url);
        if (resolved.Status == MusicServiceStatus.NotFound)
            return StreamLookup.NotFound;

        if (resolved.Channel is not { Id: { } id } channel)
            return StreamLookup.Unavailable;

        // The service could not check the channel now, but the resolve found it, so the next check fills its state.
        var live = await _music.GetLiveAsync([id]);
        var entry = FindInternal(live, id);
        if (entry?.Status == STATUS_MISSING)
            return StreamLookup.NotFound;

        // An offline channel has no name in the live answer, so the name of the resolve stays.
        var found = entry?.Status is STATUS_LIVE or STATUS_OFFLINE
            ? ToStreamData(id, entry with
              {
                  Name = string.IsNullOrWhiteSpace(entry.Name) ? channel.Name : entry.Name,
                  Avatar = entry.Avatar ?? channel.Avatar
              })
            : ToStreamData(id, new MusicServiceLiveChannel(id, STATUS_OFFLINE, channel.Name, channel.Avatar, null));

        if (string.IsNullOrWhiteSpace(found.Name))
            found.Name = id;

        return StreamLookup.Found(found);
    }

    private async Task<StreamData?> GetInvidiousDataByUrlInternalAsync(string url)
    {
        var match = ChannelIdRegex().Match(url);
        string? channelId;
        if (!match.Success)
        {
            var handleMatch = HandleRegex().Match(url);
            if (!handleMatch.Success)
                return null;

            var handle = handleMatch.Groups["handle"].Value;

            var instances = _scs.Data.InvidiousInstances;

            if (instances is not { Count: > 0 })
                return null;

            var invInstance = instances[_rng.Next(0, instances.Count)];

            using var client = _httpFactory.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
            client.BaseAddress = new Uri(invInstance);

            using var response = await client.GetAsync($"/@{handle}");
            if (!response.IsSuccessStatusCode)
                return null;

            channelId = response.RequestMessage?.RequestUri?.ToString().Split("/").LastOrDefault();

            if (channelId is null)
                return null;
        }
        else
        {
            channelId = match.Groups["channelid"].Value;
        }

        return await GetStreamDataAsync(channelId);
    }

    /// <summary>
    /// Gets stream data of the specified YouTube channel
    /// </summary>
    /// <param name="channelId">Channel ID or name</param>
    /// <returns><see cref="StreamData"/> of the channel. Null if none found</returns>
    public override async Task<StreamData?> GetStreamDataAsync(string channelId)
    {
        if (!UseInvidious)
            return (await GetServiceDataInternalAsync([channelId])).FirstOrDefault();

        try
        {
            var instances = _scs.Data.InvidiousInstances;

            if (instances is not { Count: > 0 })
                return null;

            var invInstance = instances[_rng.Next(0, instances.Count)];
            using var client = _httpFactory.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
            client.BaseAddress = new Uri(invInstance);

            using var channelResponse = await client.GetAsync($"/api/v1/channels/{channelId}");

            // Only a 404 means the channel is gone. An outage or a slow instance must not delete the follow.
            if (channelResponse.StatusCode == HttpStatusCode.NotFound)
            {
                _failingStreams.TryAdd(channelId, UtcNow);
                return null;
            }

            if (!channelResponse.IsSuccessStatusCode)
                return null;

            var channel = await channelResponse.Content.ReadFromJsonAsync<InvidiousChannelResponse>();
            if (channel is null)
                return null;

            // The channel answers, so it exists. A channel which is not live is not a failing channel.
            _failingStreams.TryRemove(channelId, out _);

            var response =
                await client.GetFromJsonAsync<InvChannelStreamsResponse>($"/api/v1/channels/{channelId}/streams");
            if (response is null)
                return null;

            var vid = response.Videos.FirstOrDefault(x => !x.IsUpcoming && x.LengthSeconds == 0);
            var isLive = false;
            if (vid is null)
            {
                vid = response.Videos.FirstOrDefault(x => !x.IsUpcoming);
            }
            else
            {
                isLive = true;
            }

            if (vid is null)
                return null;

            var avatarUrl = channel?.AuthorThumbnails?.Select(x => x.Url).LastOrDefault();

            return new StreamData()
            {
                Game = LIVE_GAME,
                Name = vid.Author,
                Preview = vid.Thumbnails
                    .Skip(1)
                    .Select(x => "https://i.ytimg.com/" + x.Url)
                    .FirstOrDefault(),
                Title = vid.Title,
                Viewers = vid.ViewCount,
                AvatarUrl = avatarUrl,
                IsLive = isLive,
                StreamType = FollowedStream.FType.Youtube,
                StreamUrl = WATCH_URL + vid.VideoId,
                UniqueName = vid.AuthorId,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to get stream data for a youtube channel {ChannelId}", channelId);
            return null;
        }
    }

    /// <summary>
    /// Gets stream data of all specified YouTube channels
    /// </summary>
    /// <param name="channelIds">List of channel IDs or names</param>
    /// <returns><see cref="StreamData"/> of all users, in the same order. Null for every ID not found.</returns>
    public override async Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> channelIds)
    {
        if (!UseInvidious)
            return await GetServiceDataInternalAsync(channelIds);

        var results = new List<StreamData>(channelIds.Count);
        foreach (var group in channelIds.Chunk(5))
        {
            var streamData = await Task.WhenAll(group.Select(GetStreamDataAsync));

            foreach (var data in streamData)
            {
                if (data is not null)
                    results.Add(data);
            }
        }

        return results;
    }

    // Only "missing" marks a channel as failing. An unknown state or a service error keeps the last known state,
    // so an outage of the service does not delete every YouTube follow 12 hours later.
    private async Task<List<StreamData>> GetServiceDataInternalAsync(List<string> channelIds)
    {
        var results = new List<StreamData>(channelIds.Count);
        if (!_music.IsConfigured)
            return results;

        var batch = new List<string>(MusicServiceClient.MAX_LIVE_CHANNELS);
        foreach (var id in channelIds)
        {
            if (!MusicServiceClient.IsChannelId(id))
            {
                if (_loggedNonIds.TryAdd(id, 0))
                    Log.Warning("YouTube follow {Name} is not a channel id, so the music service cannot check it", id);
                continue;
            }

            batch.Add(id);
            if (batch.Count == MusicServiceClient.MAX_LIVE_CHANNELS)
            {
                await AddServiceBatchInternalAsync(batch, results);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            await AddServiceBatchInternalAsync(batch, results);

        return results;
    }

    private async Task AddServiceBatchInternalAsync(List<string> batch, List<StreamData> results)
    {
        var live = await _music.GetLiveAsync(batch);
        if (live is null)
            return;

        foreach (var id in batch)
        {
            var entry = FindInternal(live, id);
            switch (entry?.Status)
            {
                case STATUS_LIVE or STATUS_OFFLINE:
                    _failingStreams.TryRemove(id, out _);
                    results.Add(ToStreamData(id, entry));
                    break;
                case STATUS_MISSING:
                    _failingStreams.TryAdd(id, UtcNow);
                    break;
            }
        }
    }

    private static MusicServiceLiveChannel? FindInternal(MusicServiceLiveChannel[]? channels, string id)
    {
        if (channels is null)
            return null;

        foreach (var c in channels)
        {
            if (string.Equals(c.Id, id, StringComparison.Ordinal))
                return c;
        }

        return null;
    }

    // The id is the one the bot asked for, so the key of the data matches the followed stream.
    private static StreamData ToStreamData(string id, MusicServiceLiveChannel channel)
    {
        var video = channel.Live;
        var isLive = channel.Status == STATUS_LIVE
                     && video?.VideoId is { } videoId
                     && MusicServiceClient.IsVideoId(videoId);

        return new()
        {
            StreamType = FollowedStream.FType.Youtube,
            UniqueName = id,
            // The live answer has no name for an offline channel; the check keeps the last known one.
            Name = string.IsNullOrWhiteSpace(channel.Name) ? string.Empty : channel.Name,
            AvatarUrl = HttpUrlOrNull(channel.Avatar),
            IsLive = isLive,
            Game = LIVE_GAME,
            Title = isLive ? video!.Title : null,
            Preview = isLive ? HttpUrlOrNull(video!.Thumbnail) : null,
            Viewers = isLive ? video!.Viewers ?? 0 : 0,
            StreamUrl = isLive ? WATCH_URL + video!.VideoId : CHANNEL_URL + id
        };
    }

    // Discord refuses an embed with an image link which is not http(s).
    private static string? HttpUrlOrNull(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? url
            : null;
}
public sealed class InvidiousChannelResponse
{
    [JsonPropertyName("authorId")]
    public required string AuthorId { get; init; }

    [JsonPropertyName("authorThumbnails")]
    public required List<InvAuthorThumbnail> AuthorThumbnails { get; init; }

    public sealed class InvAuthorThumbnail
    {
        [JsonPropertyName("url")]
        public required string Url { get; init; }
    }
}

public sealed class InvChannelStreamsResponse
{
    public required List<InvidiousStreamResponse> Videos { get; init; }
}

public sealed class InvidiousStreamResponse
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("videoId")]
    public required string VideoId { get; init; }

    [JsonPropertyName("lengthSeconds")]
    public required int LengthSeconds { get; init; }

    [JsonPropertyName("videoThumbnails")]
    public required List<InvidiousThumbnail> Thumbnails { get; init; }

    [JsonPropertyName("author")]
    public required string Author { get; init; }

    [JsonPropertyName("authorId")]
    public required string AuthorId { get; init; }

    [JsonPropertyName("isUpcoming")]
    public bool IsUpcoming { get; set; }

    [JsonPropertyName("viewCount")]
    public int ViewCount { get; set; }
}
