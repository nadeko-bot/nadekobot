using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using System.Buffers;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

// TikTok's web API answers without a login but refuses an IP after a few quick requests, so each request waits for a slot.
public sealed partial class TikTokProvider : Provider
{
    public static readonly TimeSpan DefaultPace = TimeSpan.FromSeconds(5);
    public const int CHECKS_PER_TICK = 5;

    private const int MIN_NAME_LENGTH = 2;
    private const int MAX_NAME_LENGTH = 24;
    private const int MAX_RESPONSE_BYTES = 256 * 1024;
    private const int MAX_GAME_NAMES = 500;
    private const int ROOM_STATUS_LIVE = 2;
    private const string USER_NOT_FOUND = "user_not_found";
    private const string SERVICE_LIVE = "live";
    private const string SERVICE_OFFLINE = "offline";
    private const string SERVICE_MISSING = "missing";
    private const string PROFILE_URL = "https://www.tiktok.com/@";
    private const string ROOM_URL =
        "https://www.tiktok.com/api-live/user/room/?aid=1988&app_name=tiktok_web&device_platform=web_pc&sourceType=54&uniqueId=";
    private const string ROOM_INFO_URL = "https://webcast.tiktok.com/webcast/room/info/?aid=1988&room_id=";
    private const string USER_AGENT =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private static readonly TimeSpan _blockTime = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _lookupMaxWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _checkMaxWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _routeRetry = TimeSpan.FromHours(1);

    private static readonly string[] _gameNameKeys = ["show_name", "full_name"];

    private static readonly SearchValues<char> _nameChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.");

    [GeneratedRegex(@"(?:^|[/.])tiktok\.com/@(?<name>[A-Za-z0-9_.]{2,24})(?=$|[/?#])", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    private readonly IHttpClientFactory _httpFactory;
    private readonly MusicServiceClient _music;
    private readonly long _paceTicks;
    private readonly ConcurrentDictionary<long, string> _gameNames = new();

    // Only the check loop touches it, so it needs no synchronization.
    private readonly Dictionary<string, long> _lastChecked = new(StringComparer.OrdinalIgnoreCase);

    private long _nextSlotTicks;
    private long _blockedUntilTicks;
    private long _serviceRouteMissingUntilTicks;

    public override FollowedStream.FType Platform
        => FollowedStream.FType.TikTok;

    public TikTokProvider(IHttpClientFactory httpFactory, MusicServiceClient music, TimeSpan? pace = null)
    {
        _httpFactory = httpFactory;
        _music = music;
        _paceTicks = (pace ?? DefaultPace).Ticks;
    }

    public static bool IsUserName(ReadOnlySpan<char> name)
        => name.Length is >= MIN_NAME_LENGTH and <= MAX_NAME_LENGTH && !name.ContainsAnyExcept(_nameChars);

    public static string LiveUrl(string name)
        => PROFILE_URL + name + "/live";

    private bool UseService
        => _music.IsConfigured && UtcNow.Ticks >= Interlocked.Read(ref _serviceRouteMissingUntilTicks);

    private bool IsBlocked
        => UtcNow.Ticks < Interlocked.Read(ref _blockedUntilTicks);

    public override Task<bool> IsValidUrl(string url)
        => Task.FromResult(UrlRegex().IsMatch(url));

    public override Task<StreamLookup> LookupByUrlAsync(string url)
    {
        var match = UrlRegex().Match(url);
        return match.Success
            ? LookupInternalAsync(match.Groups["name"].Value)
            : Task.FromResult(StreamLookup.NotFound);
    }

    public override async Task<StreamData?> GetStreamDataAsync(string login)
        => (await LookupInternalAsync(login)).Data;

    // The check loop calls this every tick. Without the service, it reads only the accounts which waited longest.
    public override async Task<IReadOnlyCollection<StreamData>> GetStreamDataAsync(List<string> usernames)
    {
        if (usernames.Count == 0)
        {
            _lastChecked.Clear();
            return [];
        }

        if (UseService && await GetServiceDataInternalAsync(usernames) is { } fromService)
            return fromService;

        return await GetScrapedDataInternalAsync(usernames);
    }

    private async Task<StreamLookup> LookupInternalAsync(string name)
    {
        if (!IsUserName(name))
            return StreamLookup.NotFound;

        if (UseService)
        {
            var res = await _music.GetTikTokLiveAsync([name]);
            if (res.Status == MusicServiceStatus.Ok)
            {
                var entry = FindInternal(res.Users, name);
                switch (entry?.Status)
                {
                    case SERVICE_LIVE or SERVICE_OFFLINE:
                        var data = ToStreamData(entry.User is { } user && IsUserName(user) ? user : name, entry);
                        if (string.IsNullOrWhiteSpace(data.Name))
                            data.Name = data.UniqueName;
                        return StreamLookup.Found(data);
                    case SERVICE_MISSING:
                        return StreamLookup.NotFound;
                    default:
                        return StreamLookup.Unavailable;
                }
            }

            if (res.Status == MusicServiceStatus.Failed)
                return StreamLookup.Unavailable;

            MarkServiceRouteMissingInternal();
        }

        return (await FetchInternalAsync(name, true, _lookupMaxWait)).Lookup;
    }

    // Null when the service has no TikTok route, so the caller reads TikTok itself.
    private async Task<List<StreamData>?> GetServiceDataInternalAsync(List<string> usernames)
    {
        var results = new List<StreamData>(usernames.Count);
        var batch = new List<string>(MusicServiceClient.MAX_TIKTOK_USERS);
        foreach (var name in usernames)
        {
            if (!IsUserName(name))
                continue;

            batch.Add(name);
            if (batch.Count < MusicServiceClient.MAX_TIKTOK_USERS)
                continue;

            if (!await AddServiceBatchInternalAsync(batch, results))
                return null;

            batch.Clear();
        }

        if (batch.Count > 0 && !await AddServiceBatchInternalAsync(batch, results))
            return null;

        return results;
    }

    // Only "missing" marks an account as failing. "unknown" or a service error keeps the last state.
    private async Task<bool> AddServiceBatchInternalAsync(List<string> batch, List<StreamData> results)
    {
        var res = await _music.GetTikTokLiveAsync(batch);
        if (res.Status == MusicServiceStatus.NotFound)
        {
            MarkServiceRouteMissingInternal();
            return false;
        }

        foreach (var name in batch)
        {
            var entry = FindInternal(res.Users, name);
            switch (entry?.Status)
            {
                case SERVICE_LIVE or SERVICE_OFFLINE:
                    _failingStreams.TryRemove(name, out _);
                    results.Add(ToStreamData(name, entry));
                    break;
                case SERVICE_MISSING:
                    _failingStreams.TryAdd(name, UtcNow);
                    break;
            }
        }

        return true;
    }

    private void MarkServiceRouteMissingInternal()
    {
        var now = UtcNow;
        if (Interlocked.Exchange(ref _serviceRouteMissingUntilTicks, (now + _routeRetry).Ticks) > now.Ticks)
            return;

        Log.Information("The music service has no TikTok route, so the bot reads TikTok itself for the next hour");
    }

    private async Task<List<StreamData>> GetScrapedDataInternalAsync(List<string> usernames)
    {
        var wanted = new HashSet<string>(usernames, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in _lastChecked)
        {
            if (!wanted.Contains(name))
                _lastChecked.Remove(name);
        }

        var due = new List<string>(usernames.Count);
        foreach (var name in usernames)
        {
            if (IsUserName(name))
                due.Add(name);
        }

        due.Sort((a, b) => _lastChecked.GetValueOrDefault(a).CompareTo(_lastChecked.GetValueOrDefault(b)));

        var results = new List<StreamData>(CHECKS_PER_TICK);
        var count = Math.Min(due.Count, CHECKS_PER_TICK);
        for (var i = 0; i < count && !IsBlocked; i++)
        {
            var name = due[i];
            var (lookup, ran) = await FetchInternalAsync(name, false, _checkMaxWait);

            // An account which TikTok did not answer, or which got no slot, goes first again in the next check.
            if (!ran || IsBlocked)
                break;

            _lastChecked[name] = UtcNow.Ticks;
            if (lookup.Data is { } data)
                results.Add(data);
        }

        return results;
    }

    // A command lookup (canonical) takes TikTok's spelling and marks nothing as failing; a check keeps the followed key.
    private async Task<(StreamLookup Lookup, bool Ran)> FetchInternalAsync(string name, bool canonical, TimeSpan maxWait)
    {
        var deadline = UtcNow + maxWait;
        if (IsBlocked || !await WaitForSlotInternalAsync(maxWait) || IsBlocked)
            return (StreamLookup.Unavailable, false);

        try
        {
            using var http = _httpFactory.CreateClient(ExternalHttp.CLIENT_NAME);
            using var cts = new CancellationTokenSource(_requestTimeout);
            // TikTok's room lookup matches only the lowercase account name.
            using var req = CreateRequest(ROOM_URL + name.ToLowerInvariant());
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                BlockInternal(res.StatusCode.ToString());
                return (StreamLookup.Unavailable, false);
            }

            if (!res.IsSuccessStatusCode)
            {
                Log.Warning("TikTok answered {StatusCode} for {TikTokUser}", (int)res.StatusCode, name);
                return (StreamLookup.Unavailable, true);
            }

            var (answer, body) = await ReadJsonInternalAsync<RoomAnswer>(res, cts.Token);
            if (body == BodyKind.Refusal)
            {
                BlockInternal("empty answer or challenge page");
                return (StreamLookup.Unavailable, false);
            }

            if (answer is null)
            {
                Log.Warning("TikTok sent a live answer for {TikTokUser} which the bot cannot read", name);
                return (StreamLookup.Unavailable, true);
            }

            if (answer.Data is not { User: { } user } room)
            {
                if (string.Equals(answer.Message, USER_NOT_FOUND, StringComparison.Ordinal))
                {
                    if (!canonical)
                        _failingStreams.TryAdd(name, UtcNow);

                    return (StreamLookup.NotFound, true);
                }

                Log.Warning("TikTok answered {Message} for {TikTokUser}", answer.Message, name);
                return (StreamLookup.Unavailable, true);
            }

            _failingStreams.TryRemove(name, out _);
            var uniqueName = canonical && user.UniqueId is { } id && IsUserName(id) ? id : name;
            var data = ToStreamData(uniqueName, user, room.LiveRoom);
            var left = deadline - UtcNow;
            if (data.IsLive && room.LiveRoom!.GameTagId != 0 && left > TimeSpan.Zero)
                data.Game = await GetGameNameInternalAsync(http, room.LiveRoom.GameTagId, user.RoomId, left);

            return (StreamLookup.Found(data), true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warning("Unable to read the TikTok live state of {TikTokUser}: {ErrorMessage}", name, ex.Message);
            return (StreamLookup.Unavailable, true);
        }
    }

    // The category name is in the room details only. Category ids do not change, so the bot asks once for each.
    private async Task<string?> GetGameNameInternalAsync(HttpClient http, long tagId, string? roomId, TimeSpan maxWait)
    {
        if (_gameNames.TryGetValue(tagId, out var known))
            return known;

        if (roomId is not { Length: > 0 } || roomId.AsSpan().ContainsAnyExceptInRange('0', '9'))
            return null;

        if (IsBlocked || !await WaitForSlotInternalAsync(maxWait) || IsBlocked)
            return null;

        try
        {
            using var cts = new CancellationTokenSource(_requestTimeout);
            using var req = CreateRequest(ROOM_INFO_URL + roomId);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                BlockInternal(res.StatusCode.ToString());
                return null;
            }

            if (!res.IsSuccessStatusCode)
                return null;

            if (await ExternalHttp.ReadLimitedAsync(res.Content, MAX_RESPONSE_BYTES, cts.Token) is not { } body)
                return null;

            string? name;
            try
            {
                name = ReadGameNameInternal(body.Buffer.AsSpan(0, body.Length), tagId);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(body.Buffer);
            }

            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (_gameNames.Count < MAX_GAME_NAMES)
                _gameNames.TryAdd(tagId, name);

            return name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Debug("Unable to read the TikTok room {RoomId}: {ErrorMessage}", roomId, ex.Message);
            return null;
        }
    }

    // Each request takes the next free slot, one per pace. False when the slot is further away than maxWait.
    private async Task<bool> WaitForSlotInternalAsync(TimeSpan maxWait)
    {
        var now = UtcNow.Ticks;
        long slot;
        while (true)
        {
            var next = Interlocked.Read(ref _nextSlotTicks);
            slot = Math.Max(now, next);
            if (slot - now > maxWait.Ticks)
                return false;

            if (Interlocked.CompareExchange(ref _nextSlotTicks, slot + _paceTicks, next) == next)
                break;
        }

        if (slot > now)
            await Task.Delay(TimeSpan.FromTicks(slot - now), Time);

        return true;
    }

    private void BlockInternal(string reason)
    {
        Interlocked.Exchange(ref _blockedUntilTicks, (UtcNow + _blockTime).Ticks);
        Log.Warning("TikTok refused a live check ({Reason}), so TikTok checks pause for {Seconds} seconds",
            reason,
            (int)_blockTime.TotalSeconds);
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", USER_AGENT);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.tiktok.com/");
        return req;
    }

    private enum BodyKind
    {
        Json,
        // TikTok answers an IP which it refuses with an empty body or a challenge page.
        Refusal,
        // Too large, or JSON in a shape which the bot does not know. TikTok did not refuse the request.
        Unreadable
    }

    private static async Task<(T? Value, BodyKind Kind)> ReadJsonInternalAsync<T>(HttpResponseMessage res, CancellationToken ct)
        where T : class
    {
        if (await ExternalHttp.ReadLimitedAsync(res.Content, MAX_RESPONSE_BYTES, ct) is not { } body)
        {
            Log.Warning("A TikTok answer is larger than {MaxBytes} bytes", MAX_RESPONSE_BYTES);
            return (null, BodyKind.Unreadable);
        }

        try
        {
            var json = body.Buffer.AsSpan(0, body.Length).TrimStart(" \t\r\n"u8);
            if (json.IsEmpty || json[0] is not ((byte)'{' or (byte)'['))
                return (null, BodyKind.Refusal);

            return (JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web), BodyKind.Json);
        }
        catch (JsonException)
        {
            return (null, BodyKind.Unreadable);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(body.Buffer);
        }
    }

    // data.game_tag is a list of tags while the room has a category, and an empty object when it has none.
    private static string? ReadGameNameInternal(ReadOnlySpan<byte> json, long tagId)
    {
        if (json.IsEmpty)
            return null;

        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("game_tag", out var tags))
                return null;

            if (tags.ValueKind == JsonValueKind.Object)
                return TagName(tags, tagId);

            if (tags.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var tag in tags.EnumerateArray())
            {
                if (TagName(tag, tagId) is { } name)
                    return name;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        static string? TagName(JsonElement tag, long tagId)
        {
            if (tag.ValueKind != JsonValueKind.Object
                || !tag.TryGetProperty("id", out var id)
                || !id.TryGetInt64(out var value)
                || value != tagId)
                return null;

            foreach (var prop in _gameNameKeys)
            {
                if (tag.TryGetProperty(prop, out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s)
                    return s;
            }

            return null;
        }
    }

    private static MusicServiceTikTokUser? FindInternal(MusicServiceTikTokUser[] users, string name)
    {
        foreach (var u in users)
        {
            if (string.Equals(u.User, name, StringComparison.OrdinalIgnoreCase))
                return u;
        }

        return null;
    }

    private static StreamData ToStreamData(string uniqueName, RoomUser user, LiveRoom? room)
    {
        var isLive = room?.Status == ROOM_STATUS_LIVE;
        return new()
        {
            StreamType = FollowedStream.FType.TikTok,
            UniqueName = uniqueName,
            Name = string.IsNullOrWhiteSpace(user.Nickname) ? uniqueName : user.Nickname,
            AvatarUrl = HttpUrlOrNull(user.AvatarMedium ?? user.AvatarThumb),
            IsLive = isLive,
            // An ended room keeps its title, viewer count and cover.
            Title = isLive ? room!.Title : null,
            Viewers = isLive ? room!.LiveRoomStats?.UserCount ?? 0 : 0,
            Preview = isLive ? HttpUrlOrNull(room!.CoverUrl) : null,
            StreamUrl = LiveUrl(uniqueName)
        };
    }

    // An offline account can come back without a name; NotifChecker.ApplyCheck fills the last known one.
    private static StreamData ToStreamData(string uniqueName, MusicServiceTikTokUser entry)
    {
        var live = entry.Status == SERVICE_LIVE ? entry.Live : null;
        return new()
        {
            StreamType = FollowedStream.FType.TikTok,
            UniqueName = uniqueName,
            Name = string.IsNullOrWhiteSpace(entry.Name) ? string.Empty : entry.Name,
            AvatarUrl = HttpUrlOrNull(entry.Avatar),
            IsLive = live is not null,
            Title = live?.Title,
            Viewers = live?.Viewers ?? 0,
            Preview = HttpUrlOrNull(live?.Cover),
            Game = live?.Game,
            StreamUrl = LiveUrl(uniqueName)
        };
    }

    private sealed record RoomAnswer(RoomData? Data, string? Message);

    private sealed record RoomData(RoomUser? User, LiveRoom? LiveRoom);

    private sealed record RoomUser(string? UniqueId, string? Nickname, string? AvatarMedium, string? AvatarThumb, string? RoomId);

    private sealed record LiveRoom(int Status, string? Title, string? CoverUrl, long GameTagId, LiveRoomStats? LiveRoomStats);

    private sealed record LiveRoomStats(int UserCount);
}
