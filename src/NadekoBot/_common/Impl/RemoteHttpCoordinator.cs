using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Services;

// Talks to NadekoBot.Coordinator with JSON over HTTP/2 without TLS (src/NadekoBot.Coordinator/README.md).
public sealed class RemoteHttpCoordinator : ICoordinator, IReadyExecutor, IDisposable
{
    public const string SHARD_TOKEN_ENV = "NADEKOBOT_SHARD_TOKEN";
    public const string HTTP_CLIENT_NAME = "coordinator";

    private const string DEFAULT_URL = "http://localhost:3442";
    private const string CONNECTED = "connected";
    private const string CONNECTING = "connecting";
    private const string DISCONNECTED = "disconnected";
    private const string UNKNOWN_SHARD = "unknown_shard";
    private const string REPLACED = "replaced";
    private const int EXIT_WITHOUT_COORDINATOR = 1;

    private const int SIGHUP = 1;
    private const nint SIG_IGN = 1;
    private const nint SIG_ERR = -1;

    private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(7.5);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // A coordinator which crashed or restarts takes the shard over again; only a shard alone this long exits.
    private static readonly TimeSpan _aloneLimit = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly DiscordSocketClient _client;
    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();

    public RemoteHttpCoordinator(IBotCreds creds, DiscordSocketClient client, IHttpClientFactory httpFactory)
    {
        var url = string.IsNullOrWhiteSpace(creds.CoordinatorUrl) ? DEFAULT_URL : creds.CoordinatorUrl;
        _http = httpFactory.CreateClient(HTTP_CLIENT_NAME);
        _http.BaseAddress = new Uri(url);
        _client = client;
        _token = Environment.GetEnvironmentVariable(SHARD_TOKEN_ENV) ?? string.Empty;
        if (_token.Length == 0)
            Log.Warning("{Env} is not set, so the coordinator refuses the heartbeats of this shard. Let the coordinator start it",
                SHARD_TOKEN_ENV);
    }

    // The coordinator often runs as the command of a terminal (tmux). When it exits, the kernel sends SIGHUP to its process
    // group, which holds the shards and their ffmpeg processes. SIG_IGN, unlike a PosixSignalRegistration handler, also holds in child processes.
    public static void IgnoreHangup()
    {
        if (OperatingSystem.IsWindows())
            return;

        if (SetSignal(SIGHUP, SIG_IGN) == SIG_ERR)
            Log.Warning("Could not ignore SIGHUP (errno {Errno}). The shard stops when the coordinator's terminal closes",
                Marshal.GetLastPInvokeError());
    }

    [DllImport("libc", EntryPoint = "signal", SetLastError = true)]
    private static extern nint SetSignal(int signal, nint handler);

    public bool IsStopped
        => _stop.IsCancellationRequested;

    // The coordinator holds the request until a vote waits, at most a few seconds, so shard 0 calls this in a loop.
    public async Task<CoordVote[]> TakeVotesAsync()
    {
        var token = _stop.Token;
        using var res = await _http.PostAsJsonAsync("coord/votes/take",
            new CoordTakeVotesBody(_token),
            CoordClientJsonContext.Default.CoordTakeVotesBody,
            token);
        res.EnsureSuccessStatusCode();
        var reply = await res.Content.ReadFromJsonAsync(CoordClientJsonContext.Default.CoordVotesReply, token);
        return reply?.Votes ?? [];
    }

    public async ValueTask<bool> RestartBotAsync()
    {
        using var res = await _http.PostAsJsonAsync("coord/restart-all",
            new CoordRestartAllBody(false),
            CoordClientJsonContext.Default.CoordRestartAllBody);
        return res.IsSuccessStatusCode;
    }

    public async ValueTask DieAsync(bool graceful)
    {
        using var res = await _http.PostAsJsonAsync("coord/die",
            new CoordDieBody(graceful),
            CoordClientJsonContext.Default.CoordDieBody);
        res.EnsureSuccessStatusCode();
    }

    public async ValueTask<bool> RestartShardAsync(int shardId)
    {
        using var res = await _http.PostAsync($"coord/shards/{shardId}/restart", null);
        return res.IsSuccessStatusCode;
    }

    public async ValueTask<IList<ShardStatus>> GetAllShardStatusesAsync()
    {
        var reply = await _http.GetFromJsonAsync("coord/shards", CoordClientJsonContext.Default.CoordShardsReply);
        var shards = reply?.Shards ?? [];
        var result = new ShardStatus[shards.Length];
        for (var i = 0; i < shards.Length; i++)
        {
            var s = shards[i];
            result[i] = new ShardStatus
            {
                ShardId = s.ShardId,
                ConnectionState = FromStateName(s.State),
                GuildCount = s.GuildCount,
                LastUpdate = s.LastUpdate,
                UptimePercent = s.UptimePercent
            };
        }

        return result;
    }

    public async ValueTask<int> GetGuildCountAsync()
    {
        var reply = await _http.GetFromJsonAsync("coord/shards", CoordClientJsonContext.Default.CoordShardsReply);
        return reply?.GuildCount ?? 0;
    }

    public async Task Reload()
    {
        using var res = await _http.PostAsync("coord/reload", null);
        res.EnsureSuccessStatusCode();
    }

    public Task OnReadyAsync()
    {
        _ = Task.Run(HeartbeatLoopAsync);
        return Task.CompletedTask;
    }

    private async Task HeartbeatLoopAsync()
    {
        var lastOk = DateTime.UtcNow;
        var failing = false;
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var connected = _client.ConnectionState == ConnectionState.Connected;
                using var res = await _http.PostAsJsonAsync($"coord/shards/{_client.ShardId}/heartbeat",
                    new CoordHeartbeatBody(_token, connected ? _client.Guilds.Count : 0, ToStateName(_client.ConnectionState)),
                    CoordClientJsonContext.Default.CoordHeartbeatBody,
                    token);

                var code = res.IsSuccessStatusCode ? null : await ReadErrorAsync(res, token);
                if (res.IsSuccessStatusCode)
                {
                    if (failing)
                        Log.Information("The coordinator answers again");

                    failing = false;
                    lastOk = DateTime.UtcNow;
                }
                else if (code is UNKNOWN_SHARD or REPLACED)
                {
                    // The coordinator has fewer shards now, or another process runs this shard.
                    Log.Error("The coordinator does not run this process as shard {ShardId} ({Code}). It exits",
                        _client.ShardId,
                        code);
                    break;
                }
                else
                {
                    // Any other answer is a wrong coordinatorUrl or a broken coordinator, not a reason to stop the shard.
                    if (!failing)
                        Log.Error("The coordinator answered {Status} to the heartbeat. Check coordinatorUrl in creds.yml",
                            (int)res.StatusCode);

                    failing = true;
                    if (DateTime.UtcNow - lastOk > _aloneLimit)
                    {
                        Log.Error("No heartbeat answer for {Limit}. The shard exits", _aloneLimit);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (!failing)
                    Log.Warning("The coordinator does not answer: {Message}. The shard keeps running", ex.Message);

                failing = true;
                if (DateTime.UtcNow - lastOk > _aloneLimit)
                {
                    Log.Error("No coordinator for {Limit}. The shard exits", _aloneLimit);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected heartbeat exception: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(_heartbeatInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!token.IsCancellationRequested)
            Environment.Exit(EXIT_WITHOUT_COORDINATOR);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage res, CancellationToken token)
    {
        try
        {
            var error = await res.Content.ReadFromJsonAsync(CoordClientJsonContext.Default.CoordErrorBody, token);
            return error?.Error;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }

    private static string ToStateName(ConnectionState state)
        => state switch
        {
            ConnectionState.Connecting => CONNECTING,
            ConnectionState.Connected => CONNECTED,
            _ => DISCONNECTED
        };

    private static ConnectionState FromStateName(string? state)
        => state switch
        {
            CONNECTING => ConnectionState.Connecting,
            CONNECTED => ConnectionState.Connected,
            _ => ConnectionState.Disconnected
        };

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _http.Dispose();
    }
}

public sealed record CoordHeartbeatBody(string Token, int GuildCount, string State);

public sealed record CoordRestartAllBody(bool Nuke);

public sealed record CoordDieBody(bool Graceful);

public sealed record CoordErrorBody(string? Error);

public sealed record CoordShardReply(int ShardId, string State, int GuildCount, double UptimePercent, DateTime LastUpdate);

public sealed record CoordShardsReply(int TotalShards, int GuildCount, CoordShardReply[] Shards);

public sealed record CoordTakeVotesBody(string Token);

public sealed record CoordVote(string UserId, string Type);

public sealed record CoordVotesReply(CoordVote[] Votes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CoordHeartbeatBody))]
[JsonSerializable(typeof(CoordRestartAllBody))]
[JsonSerializable(typeof(CoordDieBody))]
[JsonSerializable(typeof(CoordErrorBody))]
[JsonSerializable(typeof(CoordShardsReply))]
[JsonSerializable(typeof(CoordTakeVotesBody))]
[JsonSerializable(typeof(CoordVotesReply))]
public sealed partial class CoordClientJsonContext : JsonSerializerContext
{
}
