#nullable enable
using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog;

namespace NadekoBot.Coordinator.Api;

// The routes which the shards and the bot's owner commands call; JSON over HTTP/2 without TLS on the coordinator port.
public static class ShardProtocol
{
    public const string PATH = "/coord";

    private const string UNKNOWN_SHARD = "unknown_shard";
    private const string REPLACED = "replaced";
    private const string INVALID_VALUE = "invalid_value";
    private const string QUEUE_FULL = "queue_full";

    // Lets the reply reach the caller before the process exits.
    private static readonly TimeSpan _exitDelay = TimeSpan.FromMilliseconds(250);

    public static void MapShardProtocol(this IEndpointRouteBuilder endpoints)
    {
        var coord = endpoints.MapGroup(PATH);

        coord.MapPost("/shards/{shardId:int}/heartbeat", Heartbeat);
        coord.MapGet("/shards", static (CoordinatorRunner runner)
            => Results.Json(CoordApi.BuildStatus(runner), CoordJsonContext.Default.CoordStatusDto));
        coord.MapPost("/shards/{shardId:int}/restart", RestartShard);
        coord.MapPost("/restart-all", static (RestartAllRequest body, CoordinatorRunner runner) =>
        {
            Log.Warning("All shards restart (nuke: {Nuke})", body.Nuke);
            runner.RestartAll(body.Nuke);
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });
        coord.MapPost("/die", static (DieRequest body, CoordinatorRunner runner) =>
        {
            Log.Warning("The coordinator stops (graceful: {Graceful})", body.Graceful);
            runner.Stop(body.Graceful);
            _ = Task.Run(static async () =>
            {
                await Task.Delay(_exitDelay);
                Environment.Exit(0);
            });
            return Results.StatusCode(StatusCodes.Status202Accepted);
        });
        coord.MapPost("/reload", static (CoordinatorRunner runner, ShardApiSettings apiSettings) =>
        {
            runner.ReloadConfig();
            apiSettings.Reload();
            return Results.NoContent();
        });
        coord.MapPost("/votes", AddVote);
        coord.MapPost("/votes/take", TakeVotesAsync);
    }

    // The shard routes have no key, so anyone who reaches the port can stop the bot.
    public static void WarnIfNotLoopback(IServerAddressesFeature? addresses, CoordApiEndpoint api)
    {
        if (addresses is null)
            return;

        foreach (var address in addresses.Addresses)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (api.Enabled && uri.Port == api.Port))
                continue;

            var loopback = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                           || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
            if (!loopback)
                Log.Error("The shard port listens on {Address}. Keep it on loopback: its routes have no key", address);
        }
    }

    private static Task Heartbeat(HttpContext http, int shardId, HeartbeatRequest body, CoordinatorRunner runner)
    {
        if (!TryParseState(body.State, out var state) || body.GuildCount < 0)
            return CoordApi.WriteErrorAsync(http, StatusCodes.Status400BadRequest, INVALID_VALUE);

        switch (runner.Heartbeat(shardId, body.Token, body.GuildCount, state))
        {
            case HeartbeatResult.UnknownShard:
                return CoordApi.WriteErrorAsync(http, StatusCodes.Status404NotFound, UNKNOWN_SHARD);
            case HeartbeatResult.Replaced:
                Log.Warning("A process which the coordinator does not run sent a heartbeat for shard {ShardId}", shardId);
                return CoordApi.WriteErrorAsync(http, StatusCodes.Status409Conflict, REPLACED);
            default:
                http.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
        }
    }

    private static Task RestartShard(HttpContext http, int shardId, CoordinatorRunner runner)
    {
        if (!runner.RestartShard(shardId))
            return CoordApi.WriteErrorAsync(http, StatusCodes.Status404NotFound, UNKNOWN_SHARD);

        http.Response.StatusCode = StatusCodes.Status202Accepted;
        return Task.CompletedTask;
    }

    private static Task AddVote(HttpContext http, VoteRequest body, VoteRelay votes)
    {
        if (!ulong.TryParse(body.UserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || userId == 0
            || !VoteRelay.TryGetType(body.Type, out var type))
            return CoordApi.WriteErrorAsync(http, StatusCodes.Status400BadRequest, INVALID_VALUE);

        if (!votes.TryAdd(userId, type))
        {
            Log.Warning("The vote queue is full ({Capacity} votes). Shard {ShardId} does not take them",
                VoteRelay.CAPACITY,
                VoteRelay.SHARD_ID);
            return CoordApi.WriteErrorAsync(http, StatusCodes.Status503ServiceUnavailable, QUEUE_FULL);
        }

        http.Response.StatusCode = StatusCodes.Status202Accepted;
        return Task.CompletedTask;
    }

    private static async Task TakeVotesAsync(HttpContext http, TakeVotesRequest body, CoordinatorRunner runner, VoteRelay votes)
    {
        if (!runner.IsShardProcess(VoteRelay.SHARD_ID, body.Token))
        {
            await CoordApi.WriteErrorAsync(http, StatusCodes.Status409Conflict, REPLACED);
            return;
        }

        await votes.WaitAsync(http.RequestAborted);
        if (http.RequestAborted.IsCancellationRequested)
            return;

        // The coordinator may have started another shard 0 during the wait; the queue keeps the votes for it.
        if (!runner.IsShardProcess(VoteRelay.SHARD_ID, body.Token))
        {
            await CoordApi.WriteErrorAsync(http, StatusCodes.Status409Conflict, REPLACED);
            return;
        }

        await http.Response.WriteAsJsonAsync(new VotesDto(votes.Take()), CoordJsonContext.Default.VotesDto);
    }

    private static bool TryParseState(string? text, out ConnState state)
    {
        switch (text)
        {
            case "connected":
                state = ConnState.Connected;
                return true;
            case "connecting":
                state = ConnState.Connecting;
                return true;
            case "disconnected":
                state = ConnState.Disconnected;
                return true;
            default:
                state = ConnState.Disconnected;
                return false;
        }
    }
}
