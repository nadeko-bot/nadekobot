using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Modules.Searches.Common;
using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NadekoBot.Modules.Searches.Services;

public sealed class AnimeSearchService(IBotCache cache, IHttpClientFactory httpFactory, IPubSub pubSub)
    : INService, IReadyExecutor
{
    // All shards share one IP and one AniList limit, so a 429 on one shard pauses every shard.
    private static readonly TypedKey<long> _pauseKey = new("anilist.pause");
    private const string ANILIST_GRAPHQL_URL = "https://graphql.anilist.co";
    private const string ANIME = "ANIME";
    private const string MANGA = "MANGA";
    private const int MAX_QUERY_LENGTH = 100;
    private const int MAX_RESPONSE_BYTES = 256 * 1024;

    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _minRateLimitPause = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _defaultRateLimitPause = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _maxRateLimitPause = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _userExpiry = TimeSpan.FromHours(1);
    private static readonly TimeSpan _animeExpiry = TimeSpan.FromHours(12);
    private static readonly TimeSpan _mangaExpiry = TimeSpan.FromHours(3);

    private const string ANILIST_USER_QUERY = """
        query ($name: String) {
          User(name: $name) {
            id
            name
            siteUrl
            avatar { large }
            bannerImage
            about
            statistics {
              anime { count meanScore minutesWatched episodesWatched }
              manga { count meanScore chaptersRead volumesRead }
            }
            favourites {
              anime(page: 1, perPage: 2) { nodes { title { romaji english } } }
              manga(page: 1, perPage: 2) { nodes { title { romaji english } } }
              characters(page: 1, perPage: 2) { nodes { name { full } } }
            }
          }
        }
        """;

    private const string ANILIST_MEDIA_QUERY = """
        query ($q: String, $type: MediaType) {
          Media(search: $q, type: $type, isAdult: false) {
            id
            siteUrl
            status
            episodes
            chapters
            volumes
            averageScore
            genres
            title { english romaji }
            description(asHtml: false)
            coverImage { extraLarge large }
          }
        }
        """;

    // Environment.TickCount64 until which the bot sends no request, after AniList answered 429.
    private long _pausedUntilMs;

    public Task OnReadyAsync()
    {
        _ = pubSub.Sub(_pauseKey, OnPauseAsync);
        return Task.CompletedTask;
    }

    private ValueTask OnPauseAsync(long pauseMs)
    {
        PauseForInternal(pauseMs);
        return default;
    }

    public async Task<AnilistUser?> GetAnilistUserAsync(string username)
    {
        if (NormalizeQuery(username) is not { } name)
            return null;

        var cacheKey = new TypedKey<AnilistUser>(CacheKey("anilist_user:", name));

        var cached = await cache.GetAsync(cacheKey);
        if (cached.TryPickT0(out var cachedUser, out _))
            return cachedUser;

        var result = await PostInternalAsync<AnilistUserResponse>(ANILIST_USER_QUERY, new { name });
        var user = result?.Data?.User;

        if (user is not null)
            await cache.AddAsync(cacheKey, user, expiry: _userExpiry);

        return user;
    }

    public async Task<AnimeResult?> GetAnimeData(string query)
    {
        if (NormalizeQuery(query) is not { } q)
            return null;

        var key = new TypedKey<AnimeResult>(CacheKey("anilist_anime:", q));
        var cached = await cache.GetAsync(key);
        if (cached.TryPickT0(out var data, out _))
            return data;

        if (await SearchMediaInternalAsync(q, ANIME) is not { } media)
            return null;

        data = new()
        {
            Id = media.Id,
            TitleEnglish = GetTitle(media),
            AiringStatus = media.Status switch
            {
                "FINISHED" => "Finished Airing",
                "RELEASING" => "Currently Airing",
                "NOT_YET_RELEASED" => "Not Yet Aired",
                "CANCELLED" => "Cancelled",
                "HIATUS" => "On Hiatus",
                _ => "Unknown"
            },
            TotalEpisodes = media.Episodes ?? 0,
            Description = media.Description?.TrimTo(AnimeResult.SYNOPSIS_LENGTH),
            ImageUrlLarge = GetCover(media),
            Genres = media.Genres ?? [],
            AverageScore = media.AverageScore ?? 0,
            Link = media.SiteUrl ?? $"https://anilist.co/anime/{media.Id}"
        };

        await cache.AddAsync(key, data, expiry: _animeExpiry);
        return data;
    }

    public async Task<MangaResult?> GetMangaData(string query)
    {
        if (NormalizeQuery(query) is not { } q)
            return null;

        var key = new TypedKey<MangaResult>(CacheKey("anilist_manga:", q));
        var cached = await cache.GetAsync(key);
        if (cached.TryPickT0(out var data, out _))
            return data;

        if (await SearchMediaInternalAsync(q, MANGA) is not { } media)
            return null;

        data = new()
        {
            Id = media.Id,
            TitleEnglish = GetTitle(media),
            PublishingStatus = media.Status switch
            {
                "FINISHED" => "Finished",
                "RELEASING" => "Publishing",
                "NOT_YET_RELEASED" => "Not Yet Published",
                "CANCELLED" => "Cancelled",
                "HIATUS" => "On Hiatus",
                _ => "Unknown"
            },
            TotalChapters = media.Chapters ?? 0,
            TotalVolumes = media.Volumes ?? 0,
            Description = media.Description?.TrimTo(AnimeResult.SYNOPSIS_LENGTH),
            ImageUrlLge = GetCover(media),
            Genres = media.Genres ?? [],
            AverageScore = media.AverageScore ?? 0,
            Link = media.SiteUrl ?? $"https://anilist.co/manga/{media.Id}"
        };

        await cache.AddAsync(key, data, expiry: _mangaExpiry);
        return data;
    }

    private async Task<AnilistMedia?> SearchMediaInternalAsync(string query, string type)
        => (await PostInternalAsync<AnilistMediaResponse>(ANILIST_MEDIA_QUERY, new { q = query, type }))?.Data?.Media;

    // Every failure returns null, so the command shows its "not found" message.
    private async Task<T?> PostInternalAsync<T>(string query, object variables)
        where T : class
    {
        if (Environment.TickCount64 < Volatile.Read(ref _pausedUntilMs))
            return null;

        try
        {
            using var cts = new CancellationTokenSource(_requestTimeout);
            using var http = httpFactory.CreateClient(ExternalHttp.CLIENT_NAME);
            using var req = new HttpRequestMessage(HttpMethod.Post, ANILIST_GRAPHQL_URL);
            req.Content = JsonContent.Create(new { query, variables });
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await PauseAllShardsInternalAsync(res);
                return null;
            }

            if (!res.IsSuccessStatusCode)
            {
                // A search without a match answers 404.
                if (res.StatusCode != HttpStatusCode.NotFound)
                    Log.Warning("AniList answered with status {StatusCode}", (int)res.StatusCode);

                return null;
            }

            if (await ExternalHttp.ReadLimitedAsync(res.Content, MAX_RESPONSE_BYTES, cts.Token) is not { } body)
            {
                Log.Warning("AniList sent an answer larger than {MaxBytes} bytes", MAX_RESPONSE_BYTES);
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body.Buffer.AsSpan(0, body.Length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(body.Buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("AniList request failed: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    private async Task PauseAllShardsInternalAsync(HttpResponseMessage res)
    {
        var retryAfter = res.Headers.RetryAfter;
        var wait = retryAfter?.Delta
                   ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                   ?? _defaultRateLimitPause;
        wait = TimeSpan.FromTicks(Math.Clamp(wait.Ticks, _minRateLimitPause.Ticks, _maxRateLimitPause.Ticks));

        var pauseMs = (long)wait.TotalMilliseconds;
        PauseForInternal(pauseMs);
        Log.Warning("AniList rate limit reached. AniList searches pause for {Seconds} seconds on all shards",
            (int)wait.TotalSeconds);

        await pubSub.Pub(_pauseKey, pauseMs);
    }

    // Only moves the end forward, so a shorter pause from another shard cannot end a longer one.
    private void PauseForInternal(long pauseMs)
    {
        var until = Environment.TickCount64 + pauseMs;
        var current = Volatile.Read(ref _pausedUntilMs);
        while (until > current)
        {
            var seen = Interlocked.CompareExchange(ref _pausedUntilMs, until, current);
            if (seen == current)
                return;

            current = seen;
        }
    }

    private static string? NormalizeQuery(string? query)
    {
        var q = query.AsSpan().Trim();
        if (q.Length > MAX_QUERY_LENGTH)
            q = q[..MAX_QUERY_LENGTH].TrimEnd();

        return q.IsEmpty ? null : q.ToString();
    }

    private static string CacheKey(string prefix, string query)
        => string.Create(prefix.Length + query.Length,
            (prefix, query),
            static (span, s) =>
            {
                s.prefix.CopyTo(span);
                s.query.AsSpan().ToLowerInvariant(span[s.prefix.Length..]);
            });

    private static string GetTitle(AnilistMedia media)
        => !string.IsNullOrWhiteSpace(media.Title?.English)
            ? media.Title.English
            : media.Title?.Romaji ?? "?";

    private static string? GetCover(AnilistMedia media)
        => media.CoverImage?.ExtraLarge ?? media.CoverImage?.Large;
}
