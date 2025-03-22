using NadekoBot.Db.Models;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

/// <summary>
/// Provider for tracking YouTube livestreams
/// </summary>
public sealed partial class YouTubeProvider : Provider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SearchesConfigService _scs;
    private readonly NadekoRandom _rng = new();

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

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeProvider"/> class.
    /// </summary>
    /// <param name="httpFactory">The HTTP client factory to create HTTP clients.</param>
    public YouTubeProvider(
        IHttpClientFactory httpFactory,
        SearchesConfigService scs
    )
    {
        _httpFactory = httpFactory;
        _scs = scs;
    }

    /// <summary>
    /// Checks whether the specified url is a valid YouTube url.
    /// </summary>
    /// <param name="url">Url to check</param>
    /// <returns>True if valid, otherwise false</returns>
    public override Task<bool> IsValidUrl(string url)
    {
        var success = HandleRegex().IsMatch(url)
                      || ChannelIdRegex().IsMatch(url);

        return Task.FromResult(success);
    }

    /// <summary>
    /// Gets stream data of the stream on the specified YouTube url
    /// </summary>
    /// <param name="url">Url of the stream</param>
    /// <returns><see cref="StreamData"/> of the specified stream. Null if none found</returns>
    public override async Task<StreamData?> GetStreamDataByUrlAsync(string url)
    {
        var match = ChannelIdRegex().Match(url);
        var channelId = string.Empty;
        if (!match.Success)
        {
            var handleMatch = HandleRegex().Match(url);
            if (!handleMatch.Success)
                return null;

            var handle = handleMatch.Groups["handle"].Value;

            var instances = _scs.Data.InvidiousInstances;

            if (instances is not { Count: > 0 })
                return null;

            var invInstance = instances[_rng.Next(0, _scs.Data.InvidiousInstances.Count)];

            using var client = _httpFactory.CreateClient();
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
        try
        {
            var instances = _scs.Data.InvidiousInstances;

            if (instances is not { Count: > 0 })
                return null;

            var invInstance = instances[_rng.Next(0, instances.Count)];
            var client = _httpFactory.CreateClient();
            client.BaseAddress = new Uri(invInstance);

            var channel = await client.GetFromJsonAsync<InvidiousChannelResponse>($"/api/v1/channels/{channelId}");
            if (channel is null)
                return null;

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
                Game = "Livestream",
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
                StreamUrl = "https://youtube.com/watch?v=" + vid.VideoId,
                UniqueName = vid.AuthorId,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to get stream data for a youtube channel {ChannelId}", channelId);
            _failingStreams.TryAdd(channelId, DateTime.UtcNow);
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