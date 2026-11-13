using System.Buffers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NadekoBot.Modules.Searches;

// The client of the finance sites. The bot chooses the hosts. The text of a member goes only into a checked symbol or an escaped search value.
public static class FinanceHttp
{
    public const string CLIENT_NAME = "finance";
    public const int MAX_RESPONSE_BYTES = 512 * 1024;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    public static void Configure(HttpClient http)
    {
        http.Timeout = _timeout;
        http.DefaultRequestVersion = HttpVersion.Version20;
        http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.5");
    }

    public static SocketsHttpHandler CreateHandler()
        => new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxAutomaticRedirections = ExternalHttp.MAX_REDIRECTS
        };

    // Throws HttpRequestException on an error status or a body over the limit, JsonException on bad JSON, and OperationCanceledException after the timeout.
    public static async Task<T?> GetJsonAsync<T>(IHttpClientFactory factory, string url, JsonTypeInfo<T> info)
    {
        // HttpClient.Timeout ends at the headers with ResponseHeadersRead, so this token also limits the body.
        using var timeout = new CancellationTokenSource(_timeout);
        using var http = factory.CreateClient(CLIENT_NAME);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The site answered {(int)response.StatusCode}.", null, response.StatusCode);

        var body = await ExternalHttp.ReadLimitedAsync(response.Content, MAX_RESPONSE_BYTES, timeout.Token)
                   ?? throw new HttpRequestException($"The answer is larger than {MAX_RESPONSE_BYTES} bytes.");
        try
        {
            return JsonSerializer.Deserialize(body.Buffer.AsSpan(0, body.Length), info);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(body.Buffer);
        }
    }
}

// Limits the lookups which one shard sends to a site, and stops them for a while after a failure.
public sealed class LookupGate(int maxPerWindow, TimeSpan window, TimeSpan pause, TimeProvider time)
{
    private long _pausedUntil;
    private long _windowStart;
    private int _count;

    public bool TryEnter()
    {
        var now = time.GetUtcNow().UtcTicks;
        if (now < Volatile.Read(ref _pausedUntil))
            return false;

        var start = Volatile.Read(ref _windowStart);
        if (now - start >= window.Ticks && Interlocked.CompareExchange(ref _windowStart, now, start) == start)
            Interlocked.Exchange(ref _count, 0);

        return Interlocked.Increment(ref _count) <= maxPerWindow;
    }

    public void Pause()
        => Volatile.Write(ref _pausedUntil, (time.GetUtcNow() + pause).UtcTicks);
}

// Runs one load for each key at a time, and a caller which asks for a key while it loads gets the same task.
public sealed class SingleFlight<TKey, TResult>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Task<TResult>> _running = new();

    public async Task<TResult> RunAsync<TState>(TKey key, TState state, Func<TState, TKey, Task<TResult>> load)
    {
        var mine = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = _running.GetOrAdd(key, mine.Task);
        if (running != mine.Task)
            return await running;

        try
        {
            var result = await load(state, key);
            mine.SetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            mine.SetException(ex);
            throw;
        }
        finally
        {
            _running.TryRemove(key, out _);
        }
    }
}
