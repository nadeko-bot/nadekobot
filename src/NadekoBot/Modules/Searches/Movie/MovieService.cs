using NadekoBot.Modules.Searches.Common;
using OneOf;
using System.Buffers;
using System.Net;
using System.Text.Json;

namespace NadekoBot.Modules.Searches.Services;

public enum MovieError
{
    MissingKey,
    KeyRefused,
    NotFound
}

public sealed class MovieService(IHttpClientFactory httpFactory, IBotCache cache, IBotCredsProvider creds) : INService
{
    public const string KEY_ENV_VAR = "NADEKOBOT_OMDB_API_KEY";

    private const string OMDB_URL = "https://www.omdbapi.com/";
    private const string CACHE_PREFIX = "omdb_movie:";
    private const string MOVIE_NOT_FOUND = "Movie not found!";
    private const int MAX_QUERY_LENGTH = 100;
    private const int MAX_RESPONSE_BYTES = 64 * 1024;
    private const int MAX_LOGGED_ERROR_LENGTH = 64;

    private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _expiry = TimeSpan.FromDays(1);

    public string EnvKey { private get; init; } = Environment.GetEnvironmentVariable(KEY_ENV_VAR)?.Trim() ?? string.Empty;

    public async Task<OneOf<OmdbMovie, MovieError>> GetMovieDataAsync(string name)
    {
        if (GetKey() is not { } key)
            return MovieError.MissingKey;

        var q = name.AsSpan().Trim();
        if (q.Length > MAX_QUERY_LENGTH)
            q = q[..MAX_QUERY_LENGTH].TrimEnd();

        if (q.IsEmpty)
            return MovieError.NotFound;

        var query = q.ToString();
        var cacheKey = new TypedKey<OmdbMovie>(string.Create(CACHE_PREFIX.Length + query.Length,
            query,
            static (span, s) =>
            {
                CACHE_PREFIX.CopyTo(span);
                s.AsSpan().ToLowerInvariant(span[CACHE_PREFIX.Length..]);
            }));

        var cached = await cache.GetAsync(cacheKey);
        if (cached.TryPickT0(out var movie, out _))
            return movie;

        var fetched = await FetchInternalAsync(key, query);
        if (fetched.TryPickT0(out var found, out _))
            await cache.AddAsync(cacheKey, found, expiry: _expiry);

        return fetched;
    }

    private string? GetKey()
    {
        if (EnvKey.Length > 0)
            return EnvKey;

        var key = creds.GetCreds().OmdbApiKey;
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    // The key is in the query string, so no log line may contain the url.
    private async Task<OneOf<OmdbMovie, MovieError>> FetchInternalAsync(string key, string query)
    {
        try
        {
            using var cts = new CancellationTokenSource(_requestTimeout);
            using var http = httpFactory.CreateClient(ExternalHttp.CLIENT_NAME);
            using var res = await http.GetAsync(
                $"{OMDB_URL}?apikey={Uri.EscapeDataString(key)}&t={Uri.EscapeDataString(query)}&plot=full&r=json",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            // OMDb answers a bad key or the daily limit with 401 and an Error field, so the body is read on any status.
            if (await ExternalHttp.ReadLimitedAsync(res.Content, MAX_RESPONSE_BYTES, cts.Token) is not { } body)
            {
                Log.Warning("OMDb sent an answer larger than {MaxBytes} bytes", MAX_RESPONSE_BYTES);
                return MovieError.NotFound;
            }

            OmdbMovie? movie;
            try
            {
                movie = JsonSerializer.Deserialize<OmdbMovie>(body.Buffer.AsSpan(0, body.Length));
            }
            catch (JsonException)
            {
                movie = null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(body.Buffer);
            }

            if (movie is { Title.Length: > 0 })
                return movie;

            if (string.Equals(movie?.Error, MOVIE_NOT_FOUND, StringComparison.Ordinal))
                return MovieError.NotFound;

            var error = movie?.Error is { Length: > MAX_LOGGED_ERROR_LENGTH } e ? e[..MAX_LOGGED_ERROR_LENGTH] : movie?.Error;
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                Log.Warning("OMDb refused the key: {OmdbError}. Check omdbApiKey in creds.yml or the {EnvVar} environment variable",
                    error,
                    KEY_ENV_VAR);
                return MovieError.KeyRefused;
            }

            Log.Warning("OMDb refused the request with status {StatusCode}: {OmdbError}", (int)res.StatusCode, error);
            return MovieError.NotFound;
        }
        catch (Exception ex)
        {
            Log.Warning("OMDb request failed: {ErrorMessage}", ex.Message);
            return MovieError.NotFound;
        }
    }
}
