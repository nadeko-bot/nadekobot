using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NadekoBot.Modules.Searches;

namespace NadekoBot.Modules.Music;

public sealed record MusicServiceTrack(string? Id, string? Title, int DurationSec, string? Thumbnail, string? Url);

public sealed record MusicServicePlaylist(string? Id, string? Title, MusicServiceTrack[]? Tracks);

public sealed record MusicServicePlaylistRef(string? Id, string? Title, string? Url);

public sealed record MusicServiceChannel(string? Id, string? Name, string? Avatar);

public sealed record MusicServiceLiveVideo(string? VideoId, string? Title, string? Thumbnail, int? Viewers);

// Status is "live", "offline", "missing" (no such channel) or "unknown" (the service could not check it now).
public sealed record MusicServiceLiveChannel(
    string? Id,
    string? Status,
    string? Name,
    string? Avatar,
    MusicServiceLiveVideo? Live);

public sealed record MusicServiceUpload(string? Id, string? Title, string? Thumbnail);

public enum MusicServiceStatus
{
    Ok,
    NotFound,
    Failed
}

public readonly record struct MusicServiceChannelResult(MusicServiceStatus Status, MusicServiceChannel? Channel);

public readonly record struct MusicServiceUploadsResult(MusicServiceStatus Status, MusicServiceUpload[] Videos);

// The NadekoBot music service (ndkloud docs/specs/music-service.md). It looks songs up and downloads them through
// its own exits, so a bot which uses it never connects to YouTube.
public sealed class MusicServiceClient(
    IHttpClientFactory httpFactory,
    SearchesConfigService scs,
    BotPolicy policy) : INService
{
    public const string HTTP_CLIENT_NAME = "music-service";
    public const string KEY_ENV_VAR = "NADEKOBOT_MUSIC_SERVICE_KEY";
    public const int MAX_SEARCH_LIMIT = 10;
    public const int MAX_RELATED_LIMIT = 25;
    public const int MAX_LIVE_CHANNELS = 50;

    private const int MAX_RESPONSE_BYTES = 2 * 1024 * 1024;
    private const int MAX_QUERY_LENGTH = 200;
    private const int MAX_CHANNEL_INPUT_LENGTH = 512;
    private const int CHANNEL_ID_LENGTH = 24;
    private const int MIN_PLAYLIST_ID_LENGTH = 12;
    private const int MAX_PLAYLIST_ID_LENGTH = 64;
    private const int MAX_LOGGED_ERROR_LENGTH = 64;

    private static readonly SearchValues<char> _idChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    private sealed record TrackList(MusicServiceTrack[]? Tracks);

    private sealed record PlaylistList(MusicServicePlaylistRef[]? Playlists);

    private sealed record ErrorBody(string? Error);

    private sealed record PrefetchBody(string[] Ids);

    private sealed record LiveBody(IReadOnlyList<string> Channels);

    private sealed record LiveList(MusicServiceLiveChannel[]? Channels);

    private sealed record UploadList(string? Id, MusicServiceUpload[]? Videos);

    // A 404 with another code, such as not_found for a route which an older service lacks, is not a missing channel.
    private const string ERROR_UNAVAILABLE = "unavailable";

    public static bool IsVideoId(ReadOnlySpan<char> id)
        => YoutubeHelpers.IsVideoId(id);

    public static bool IsChannelId(ReadOnlySpan<char> id)
        => id.Length == CHANNEL_ID_LENGTH && id.StartsWith("UC") && !id.ContainsAnyExcept(_idChars);

    public static bool IsPlaylistId(ReadOnlySpan<char> id)
        => id.Length is >= MIN_PLAYLIST_ID_LENGTH and <= MAX_PLAYLIST_ID_LENGTH && !id.ContainsAnyExcept(_idChars);

    // The base url without a trailing slash, or null when it is not an absolute http(s) url.
    public static string? ParseBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
            return null;

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static bool HasSameOrigin(string baseUrl, string url)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var b)
           && Uri.TryCreate(url, UriKind.Absolute, out var u)
           && string.Equals(b.Scheme, u.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(b.IdnHost, u.IdnHost, StringComparison.OrdinalIgnoreCase)
           && b.Port == u.Port;

    private sealed record ParsedBaseUrl(string Raw, string? Value);

    private volatile ParsedBaseUrl? _baseUrl;

    // A managed bot takes the url and the key only from the manager's setting, never from searches.yml or the
    // environment, because the bot owner can change those and the key is shared with other bots.
    public MusicServiceSetting Setting { private get; init; } = MusicServiceSetting.Default;

    public string? BaseUrl
    {
        get
        {
            if (policy.Managed)
                return Setting.Current?.BaseUrl;

            var raw = scs.Data.MusicServiceUrl ?? string.Empty;
            var parsed = _baseUrl;
            if (parsed is not null && string.Equals(parsed.Raw, raw, StringComparison.Ordinal))
                return parsed.Value;

            parsed = new(raw, ParseBaseUrl(raw));
            _baseUrl = parsed;
            if (parsed.Value is null && !string.IsNullOrWhiteSpace(raw))
                Log.Warning("The music service is the ytProvider, but musicServiceUrl in searches.yml is not a valid url");

            return parsed.Value;
        }
    }

    private readonly string _envKey = Environment.GetEnvironmentVariable(KEY_ENV_VAR)?.Trim() ?? string.Empty;

    // The key stays out of creds.yml, so backups and the config API never contain it.
    public string Key
    {
        private get => policy.Managed ? Setting.Current?.Key ?? string.Empty : _envKey;
        init => _envKey = value;
    }

    // The header which ffmpeg sends with a stream of the service. Never log it.
    public string? FfmpegHeaders
        => Key.Length == 0 ? null : $"Authorization: Bearer {Key}\r\n";

    public bool IsConfigured
        => BaseUrl is not null && Key.Length > 0;

    public string? GetAudioUrl(string videoId)
        => BaseUrl is { } b && IsVideoId(videoId) ? $"{b}/v1/audio/{videoId}" : null;

    // The audio of a stream url which the resolver gave, for Opus passthrough. Null when it is not WebM Opus.
    public Task<ServiceAudioStream?> OpenAudioAsync(string url, CancellationToken ct)
    {
        if (BaseUrl is not { } baseUrl || !HasSameOrigin(baseUrl, url) || Key.Length == 0)
            return Task.FromResult<ServiceAudioStream?>(null);

        return ServiceAudioStream.OpenAsync(httpFactory.CreateClient(HTTP_CLIENT_NAME), url, Key, ct);
    }

    public async Task<MusicServiceTrack[]> SearchAsync(string query, int limit)
        => await TrySearchAsync(query, limit) ?? [];

    // Null when the service did not answer, so a caller can tell a failure from no results.
    public async Task<MusicServiceTrack[]?> TrySearchAsync(string query, int limit)
    {
        var q = TrimQuery(query);
        if (q.IsEmpty)
            return [];

        var res = await GetInternalAsync<TrackList>(
            $"/v1/search?q={Uri.EscapeDataString(q.ToString())}&limit={Math.Clamp(limit, 1, MAX_SEARCH_LIMIT)}",
            "search");
        return res is null ? null : ValidTracks(res.Tracks);
    }

    public async Task<MusicServicePlaylistRef?> SearchPlaylistAsync(string query)
    {
        var q = TrimQuery(query);
        if (q.IsEmpty)
            return null;

        var res = await GetInternalAsync<PlaylistList>(
            $"/v1/search/playlists?q={Uri.EscapeDataString(q.ToString())}&limit=1",
            "playlist search");

        if (res?.Playlists is not { Length: > 0 } list || list[0].Id is not { } id || !IsPlaylistId(id))
            return null;

        return list[0];
    }

    public async Task<MusicServiceTrack?> GetTrackAsync(string videoId)
    {
        if (!IsVideoId(videoId))
            return null;

        var track = await GetInternalAsync<MusicServiceTrack>($"/v1/tracks/{videoId}", "track");
        return track is { Id: { } id, Title: not null } && IsVideoId(id) ? track : null;
    }

    public async Task<MusicServicePlaylist?> GetPlaylistAsync(string playlistId)
    {
        if (!IsPlaylistId(playlistId))
            return null;

        var res = await GetInternalAsync<MusicServicePlaylist>($"/v1/playlists/{playlistId}", "playlist");
        return res is null ? null : res with { Tracks = ValidTracks(res.Tracks) };
    }

    // Songs related to a video, without the video itself. Null when the service did not answer.
    public async Task<MusicServiceTrack[]?> GetRelatedAsync(string videoId, int limit)
    {
        if (!IsVideoId(videoId))
            return null;

        var res = await GetInternalAsync<TrackList>(
            $"/v1/related/{videoId}?limit={Math.Clamp(limit, 1, MAX_RELATED_LIMIT)}",
            "related");
        return res is null ? null : ValidTracks(res.Tracks);
    }

    // Takes a channel link, an @handle link, a /c/ or /user/ link, a channel id or an @handle.
    public async Task<MusicServiceChannelResult> ResolveChannelAsync(string input)
    {
        var q = input.AsSpan().Trim();
        if (q.IsEmpty || q.Length > MAX_CHANNEL_INPUT_LENGTH)
            return new(MusicServiceStatus.NotFound, null);

        var (res, status, error) = await SendInternalAsync<MusicServiceChannel>(
            HttpMethod.Get,
            $"/v1/channels/resolve?url={Uri.EscapeDataString(q.ToString())}",
            null,
            "channel resolve");

        if (res is { Id: { } id } && IsChannelId(id))
            return new(MusicServiceStatus.Ok, res);

        return status == HttpStatusCode.BadRequest || IsMissing(status, error)
            ? new(MusicServiceStatus.NotFound, null)
            : new(MusicServiceStatus.Failed, null);
    }

    // The newest uploads of a channel, newest first, with a valid video id each.
    public async Task<MusicServiceUploadsResult> GetUploadsAsync(string channelId)
    {
        if (!IsChannelId(channelId))
            return new(MusicServiceStatus.NotFound, []);

        var (res, status, error) = await SendInternalAsync<UploadList>(
            HttpMethod.Get,
            $"/v1/channels/{channelId}/uploads",
            null,
            "uploads");

        // A list for another channel would post its videos into this feed.
        if (res is { Id: { } id } && string.Equals(id, channelId, StringComparison.Ordinal))
            return new(MusicServiceStatus.Ok, ValidUploads(res.Videos));

        return IsMissing(status, error)
            ? new(MusicServiceStatus.NotFound, [])
            : new(MusicServiceStatus.Failed, []);
    }

    private static bool IsMissing(HttpStatusCode? status, string? error)
        => status == HttpStatusCode.NotFound && string.Equals(error, ERROR_UNAVAILABLE, StringComparison.Ordinal);

    private static MusicServiceUpload[] ValidUploads(MusicServiceUpload[]? videos)
    {
        if (videos is null or { Length: 0 })
            return [];

        var count = 0;
        foreach (var v in videos)
        {
            if (v is { Id: { } id } && IsVideoId(id))
                count++;
        }

        if (count == videos.Length)
            return videos;

        var valid = new MusicServiceUpload[count];
        var i = 0;
        foreach (var v in videos)
        {
            if (v is { Id: { } id } && IsVideoId(id))
                valid[i++] = v;
        }

        return valid;
    }

    // The live state of up to MAX_LIVE_CHANNELS channels. Null when the service did not answer.
    public async Task<MusicServiceLiveChannel[]?> GetLiveAsync(IReadOnlyList<string> channelIds)
    {
        if (channelIds.Count is 0 or > MAX_LIVE_CHANNELS)
            return null;

        var body = JsonSerializer.Serialize(new LiveBody(channelIds), JsonSerializerOptions.Web);
        var (res, _, _) = await SendInternalAsync<LiveList>(HttpMethod.Post, "/v1/live", body, "live");
        return res is null ? null : res.Channels ?? [];
    }

    // Asks the service to download a song before it plays. A failure only means the song downloads when it plays.
    public async Task PrefetchAsync(string videoId)
    {
        try
        {
            if (!IsVideoId(videoId) || BaseUrl is not { } baseUrl || Key.Length == 0)
                return;

            using var http = httpFactory.CreateClient(HTTP_CLIENT_NAME);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/prefetch");
            req.Headers.Authorization = new("Bearer", Key);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new PrefetchBody([videoId]), JsonSerializerOptions.Web),
                Encoding.UTF8,
                "application/json");

            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                Log.Debug("Music service prefetch answered {StatusCode}", (int)res.StatusCode);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Music service prefetch failed");
        }
    }

    private static ReadOnlySpan<char> TrimQuery(string query)
    {
        var q = query.AsSpan().Trim();
        return q.Length > MAX_QUERY_LENGTH ? q[..MAX_QUERY_LENGTH].TrimEnd() : q;
    }

    private static MusicServiceTrack[] ValidTracks(MusicServiceTrack[]? tracks)
    {
        if (tracks is null or { Length: 0 })
            return [];

        var count = 0;
        foreach (var t in tracks)
        {
            if (t is { Id: { } id, Title: not null } && IsVideoId(id))
                count++;
        }

        if (count == tracks.Length)
            return tracks;

        var valid = new MusicServiceTrack[count];
        var i = 0;
        foreach (var t in tracks)
        {
            if (t is { Id: { } id, Title: not null } && IsVideoId(id))
                valid[i++] = t;
        }

        return valid;
    }

    private async Task<T?> GetInternalAsync<T>(string pathAndQuery, string route)
        where T : class
        => (await SendInternalAsync<T>(HttpMethod.Get, pathAndQuery, null, route)).Value;

    // Status is null when no answer came. Error is the code of an error answer.
    private async Task<(T? Value, HttpStatusCode? Status, string? Error)> SendInternalAsync<T>(
        HttpMethod method,
        string pathAndQuery,
        string? jsonBody,
        string route)
        where T : class
    {
        if (BaseUrl is not { } baseUrl)
            return (null, null, null);

        try
        {
            using var http = httpFactory.CreateClient(HTTP_CLIENT_NAME);
            using var req = new HttpRequestMessage(method, baseUrl + pathAndQuery);
            req.Headers.Authorization = new("Bearer", Key);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (jsonBody is not null)
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (await ExternalHttp.ReadLimitedAsync(res.Content, MAX_RESPONSE_BYTES) is not { } body)
            {
                Log.Warning("The music service {Route} answer is too large", route);
                return (null, res.StatusCode, null);
            }

            try
            {
                var span = body.Buffer.AsSpan(0, body.Length);
                if (res.IsSuccessStatusCode)
                    return (JsonSerializer.Deserialize<T>(span, JsonSerializerOptions.Web), res.StatusCode, null);

                var error = ReadErrorCode(span);
                LogErrorInternal(route, res.StatusCode, error);
                return (null, res.StatusCode, error);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(body.Buffer);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warning(ex, "Music service {Route} request failed", route);
            return (null, null, null);
        }
    }

    private static string? ReadErrorCode(ReadOnlySpan<byte> body)
    {
        try
        {
            var code = JsonSerializer.Deserialize<ErrorBody>(body, JsonSerializerOptions.Web)?.Error;
            return code is { Length: > MAX_LOGGED_ERROR_LENGTH } ? code[..MAX_LOGGED_ERROR_LENGTH] : code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The service retries on its other exits, so the bot does not retry, and ignores Retry-After.
    private void LogErrorInternal(string route, HttpStatusCode status, string? code)
    {
        switch (code)
        {
            case "unavailable" or "live" or "bad_request" or "not_found":
                Log.Debug("Music service {Route}: {Error}", route, code);
                break;
            case "no_exit" or "youtube_failed":
                Log.Warning("Music service {Route} could not reach YouTube: {Error}", route, code);
                break;
            default:
                if (status == HttpStatusCode.Unauthorized && policy.Managed)
                    Log.Error("Music service key refused. The host must send a new one");
                else if (status == HttpStatusCode.Unauthorized)
                    Log.Error("Music service key refused. Check the {EnvVar} environment variable", KEY_ENV_VAR);
                else
                    Log.Warning("Music service {Route} answered {StatusCode} {Error}", route, (int)status, code);
                break;
        }
    }
}
