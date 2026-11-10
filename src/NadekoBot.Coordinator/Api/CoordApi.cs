#nullable enable
using System;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Yarp.ReverseProxy.Forwarder;

namespace NadekoBot.Coordinator.Api;

public sealed record CoordApiEndpoint(IPAddress? Address, int Port)
{
    public bool Enabled
        => Address is not null;

    public static CoordApiEndpoint From(Config config)
    {
        if (string.IsNullOrWhiteSpace(config.ApiAddress) && config.ApiPort == 0)
            return new(null, 0);

        if (!IPAddress.TryParse(config.ApiAddress, out var address) || config.ApiPort is < 1 or > 65535)
        {
            Log.Error("ApiAddress {Address} or ApiPort {Port} in coord.yml is not valid. The dashboard API is off",
                config.ApiAddress,
                config.ApiPort);
            return new(null, 0);
        }

        return new(address, config.ApiPort);
    }
}

public sealed class ShardForwarder : IDisposable
{
    public HttpMessageInvoker Invoker { get; } = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ActivityHeadersPropagator = null
    });

    public void Dispose()
        => Invoker.Dispose();
}

public static class CoordApi
{
    private const string API_PATH = "/api";
    private const string HEALTH_PATH = "/api/health";
    private const string BEARER = "Bearer ";
    private const int KEY_STACK_LIMIT = 256;
    private const int RESTARTS_PER_MINUTE = 5;

    private const string UNAUTHORIZED = "unauthorized";
    private const string NOT_FOUND = "not_found";
    private const string NOT_READY = "not_ready";
    private const string SHARD_UNAVAILABLE = "shard_unavailable";
    private const string RATE_LIMITED = "rate_limited";

    private static readonly ForwarderRequestConfig _forwardConfig = new()
    {
        ActivityTimeout = TimeSpan.FromSeconds(30)
    };

    // One budget for all shards, so a misclick or a loop in the dashboard cannot restart the whole bot.
    private static readonly FixedWindowRateLimiter _restartLimiter = new(new()
    {
        PermitLimit = RESTARTS_PER_MINUTE,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    });

    public static IServiceCollection AddCoordApi(this IServiceCollection services)
    {
        services.AddHttpForwarder();
        services.AddSingleton<ShardApiSettings>();
        services.AddSingleton<ShardForwarder>();
        return services;
    }

    // Runs before routing, so a caller without the key reaches no route and no shard.
    public static IApplicationBuilder UseCoordApiGate(this IApplicationBuilder app, CoordApiEndpoint endpoint)
    {
        var settings = app.ApplicationServices.GetRequiredService<ShardApiSettings>();
        return app.Use((http, next) =>
        {
            var path = http.Request.Path;

            // The shard protocol has no key, so the dashboard port must not reach it.
            if (path.StartsWithSegments(ShardProtocol.PATH, StringComparison.OrdinalIgnoreCase))
            {
                return endpoint.Enabled && http.Connection.LocalPort == endpoint.Port
                    ? WriteErrorAsync(http, StatusCodes.Status404NotFound, NOT_FOUND)
                    : next(http);
            }

            if (!path.StartsWithSegments(API_PATH, StringComparison.OrdinalIgnoreCase))
                return next(http);

            if (!endpoint.Enabled || http.Connection.LocalPort != endpoint.Port)
                return WriteErrorAsync(http, StatusCodes.Status404NotFound, NOT_FOUND);

            if (path.StartsWithSegments(HEALTH_PATH, StringComparison.OrdinalIgnoreCase))
                return next(http);

            if (!KeyMatches(http.Request.Headers.Authorization.ToString(), settings.Current.KeyHash))
                return WriteErrorAsync(http, StatusCodes.Status401Unauthorized, UNAUTHORIZED);

            return next(http);
        });
    }

    public static void MapCoordApi(this IEndpointRouteBuilder endpoints)
    {
        // The gate checks the local port; a Host header check would refuse a client which leaves out a default port.
        var api = endpoints.MapGroup(API_PATH);

        api.MapGet("/health", Health);
        api.MapGet("/status", Status);
        api.MapGet("/guilds/{guildId}/shard", GuildShard);
        api.MapPost("/shards/{shardId:int}/restart", RestartShard);
        api.Map("/guilds/{guildId}/{**rest}", ForwardAsync);
    }

    private static IResult Health(CoordinatorRunner runner)
    {
        var connected = runner.CountConnected(out var total);
        var status = connected == total ? "ok"
            : connected == 0 ? "disconnected"
            : "degraded";

        return Results.Json(new CoordHealthDto(status, total, connected), CoordJsonContext.Default.CoordHealthDto);
    }

    private static IResult Status(CoordinatorRunner runner)
        => Results.Json(BuildStatus(runner), CoordJsonContext.Default.CoordStatusDto);

    public static CoordStatusDto BuildStatus(CoordinatorRunner runner)
    {
        var statuses = runner.GetAllStatuses();
        var shards = new ShardDto[statuses.Count];
        var connected = 0;
        var guilds = 0;
        for (var i = 0; i < shards.Length; i++)
        {
            var s = statuses[i];
            if (IsReady(s.State, s.ShouldRestart))
                connected++;

            guilds += s.GuildCount;
            shards[i] = new(s.ShardId,
                StateName(s.State),
                s.GuildCount,
                Math.Round(s.UptimePercent, 2),
                s.StartedAtUtc(),
                s.ShouldRestart,
                s.LastUpdate);
        }

        return new CoordStatusDto(shards.Length, connected, guilds, shards);
    }

    private static Task GuildShard(HttpContext http, string guildId, CoordinatorRunner runner)
    {
        if (!TryParseId(guildId, out var id))
            return WriteErrorAsync(http, StatusCodes.Status404NotFound, NOT_FOUND);

        runner.GetGuildShard(id, out var shardId, out var state, out var scheduled);
        var name = scheduled ? "restarting" : StateName(state);
        return http.Response.WriteAsJsonAsync(new GuildShardDto(shardId, name), CoordJsonContext.Default.GuildShardDto);
    }

    private static Task RestartShard(HttpContext http, int shardId, CoordinatorRunner runner)
    {
        if (shardId < 0 || shardId >= runner.TotalShards())
            return WriteErrorAsync(http, StatusCodes.Status404NotFound, NOT_FOUND);

        using var lease = _restartLimiter.AttemptAcquire();
        if (!lease.IsAcquired)
        {
            var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var after)
                ? Math.Max(1, (int)Math.Ceiling(after.TotalSeconds))
                : 60;
            http.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
            return WriteErrorAsync(http, StatusCodes.Status429TooManyRequests, RATE_LIMITED);
        }

        // A reshard can shrink the shard list after the check.
        if (!runner.RestartShard(shardId))
            return WriteErrorAsync(http, StatusCodes.Status404NotFound, NOT_FOUND);

        Log.Warning("Shard {ShardId} restart was requested through the dashboard API", shardId);
        http.Response.StatusCode = StatusCodes.Status202Accepted;
        return Task.CompletedTask;
    }

    private static async Task ForwardAsync(
        HttpContext http,
        string guildId,
        CoordinatorRunner runner,
        IHttpForwarder forwarder,
        ShardForwarder shards,
        ShardApiSettings settings)
    {
        if (!TryParseId(guildId, out var id))
        {
            await WriteErrorAsync(http, StatusCodes.Status404NotFound, NOT_FOUND);
            return;
        }

        runner.GetGuildShard(id, out var shardId, out var state, out var scheduled);
        if (!IsReady(state, scheduled))
        {
            await WriteErrorAsync(http, StatusCodes.Status503ServiceUnavailable, NOT_READY);
            return;
        }

        var error = await forwarder.SendAsync(http,
            settings.Current.ShardPrefix(shardId),
            shards.Invoker,
            _forwardConfig,
            HttpTransformer.Default);

        if (error == ForwarderError.None)
            return;

        Log.Warning("Could not forward a dashboard request to shard {ShardId}: {Error}", shardId, error);
        if (!http.Response.HasStarted)
            await WriteErrorAsync(http, StatusCodes.Status502BadGateway, SHARD_UNAVAILABLE);
    }

    private static bool IsReady(ConnState state, bool scheduledForRestart)
        => state == ConnState.Connected && !scheduledForRestart;

    public static string StateName(ConnState state)
        => state switch
        {
            ConnState.Connected => "connected",
            ConnState.Connecting => "connecting",
            _ => "disconnected"
        };

    private static bool TryParseId(string? text, out ulong id)
        => ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);

    public static Task WriteErrorAsync(HttpContext http, int status, string code)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new CoordErrorDto(code), CoordJsonContext.Default.CoordErrorDto);
    }

    // Compares SHA-256 hashes in constant time, so the time taken does not reveal the key or its length.
    private static bool KeyMatches(ReadOnlySpan<char> header, ReadOnlySpan<byte> keyHash)
    {
        if (keyHash.IsEmpty || !header.StartsWith(BEARER, StringComparison.OrdinalIgnoreCase))
            return false;

        var token = header[BEARER.Length..].TrimStart(' ');
        var byteCount = Encoding.UTF8.GetByteCount(token);

        byte[]? rented = null;
        var buffer = byteCount <= KEY_STACK_LIMIT
            ? stackalloc byte[KEY_STACK_LIMIT]
            : rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var written = Encoding.UTF8.GetBytes(token, buffer);
            Span<byte> tokenHash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(buffer[..written], tokenHash);
            return CryptographicOperations.FixedTimeEquals(tokenHash, keyHash);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }
}
