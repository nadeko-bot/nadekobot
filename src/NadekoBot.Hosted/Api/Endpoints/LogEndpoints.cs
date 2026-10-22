using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.Hosted.Endpoints;

public static class LogEndpoints
{
    // Each flush waits for the client, so a long history does not pile up in the response.
    private const int FLUSH_BYTES = 64 * 1024;
    private const int CATCH_UP_FRAMES = 256;

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
        var count = tail ?? LogStreamService.DEFAULT_TAIL;
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

        long lastSent;
        var tailLength = Math.Min(count, LogStreamService.BUFFER_CAPACITY);
        var frames = ArrayPool<FeedFrame>.Shared.Rent(Math.Max(tailLength, CATCH_UP_FRAMES));
        try
        {
            var sent = logs.Tail(tailLength, frames.AsSpan(0, tailLength), out lastSent);
            await WriteFramesInternalAsync(body, frames, sent, ct);

            // The stream channel can drop the lines which came during a slow send; the history still has them.
            int more;
            while ((more = logs.Snapshot(lastSent, frames.AsSpan(0, CATCH_UP_FRAMES))) > 0)
            {
                await WriteFramesInternalAsync(body, frames, more, ct);
                lastSent = frames[more - 1].Id;
            }

            await body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            ArrayPool<FeedFrame>.Shared.Return(frames, clearArray: true);
        }

        await SseResponse.PumpAsync(body, subscription.Reader, lastSent, ct);
    }

    private static async ValueTask WriteFramesInternalAsync(PipeWriter body, FeedFrame[] frames, int count, CancellationToken ct)
    {
        var pending = 0;
        for (var i = 0; i < count; i++)
        {
            body.Write(frames[i].Bytes);
            pending += frames[i].Bytes.Length;
            if (pending < FLUSH_BYTES)
                continue;

            pending = 0;
            await body.FlushAsync(ct);
        }
    }
}
