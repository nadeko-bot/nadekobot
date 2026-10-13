using System.Buffers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.Hosted.Endpoints;

public static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder system)
    {
        system.MapGet("/logs", StreamLogs);
        return system;
    }

    private static async Task StreamLogs(
        HttpContext http,
        LogStreamService logs,
        IHostApplicationLifetime lifetime,
        int? tail = null)
    {
        var count = tail ?? LogStreamService.BUFFER_CAPACITY;
        if (count < 0)
        {
            await ApiErrors.WriteAsync(http, StatusCodes.Status400BadRequest, ApiErrors.INVALID_VALUE);
            return;
        }

        // Subscribing before the snapshot means no line falls between the two; duplicates are skipped by id.
        using var subscription = logs.Subscribe();
        if (subscription is null)
        {
            await ApiErrors.WriteAsync(http, StatusCodes.Status429TooManyRequests, ApiErrors.TOO_MANY_STREAMS);
            return;
        }

        SseResponse.SetHeaders(http.Response);

        // A client can stay for hours, so only a close or a shutdown ends the stream.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            http.RequestAborted,
            lifetime.ApplicationStopping);
        var ct = cts.Token;
        var body = http.Response.BodyWriter;

        long newestId;
        var frames = ArrayPool<FeedFrame>.Shared.Rent(LogStreamService.BUFFER_CAPACITY);
        try
        {
            var sent = logs.Tail(count, frames.AsSpan(0, LogStreamService.BUFFER_CAPACITY), out newestId);
            for (var i = 0; i < sent; i++)
                body.Write(frames[i].Bytes);
        }
        finally
        {
            ArrayPool<FeedFrame>.Shared.Return(frames, clearArray: true);
        }

        try
        {
            await body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SseResponse.PumpAsync(body, subscription.Reader, newestId, ct);
    }
}
