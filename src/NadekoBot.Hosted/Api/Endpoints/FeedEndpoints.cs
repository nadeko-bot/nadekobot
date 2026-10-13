using System.Buffers;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.Hosted.Endpoints;

public static class FeedEndpoints
{
    private const string LAST_EVENT_ID_HEADER = "Last-Event-ID";

    public static RouteGroupBuilder MapFeedEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/feed", StreamFeed);
        return owner;
    }

    private static async Task StreamFeed(
        HttpContext http,
        CommandFeedService feed,
        IHostApplicationLifetime lifetime,
        long since = 0)
    {
        // Subscribing before the snapshot means no frame falls between the two; duplicates are skipped by id.
        using var subscription = feed.Subscribe();
        if (subscription is null)
        {
            await ApiErrors.WriteAsync(http, StatusCodes.Status429TooManyRequests, ApiErrors.TOO_MANY_STREAMS);
            return;
        }

        SseResponse.SetHeaders(http.Response);

        // Ends the stream on shutdown, so a restart does not wait for open feeds.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            http.RequestAborted,
            lifetime.ApplicationStopping);
        var ct = cts.Token;
        var body = http.Response.BodyWriter;
        var lastSent = long.TryParse(http.Request.Headers[LAST_EVENT_ID_HEADER], out var lastEventId)
            ? lastEventId
            : since;

        try
        {
            lastSent = WriteSnapshotInternal(body, feed, lastSent);
            await body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SseResponse.PumpAsync(body, subscription.Reader, lastSent, ct);
    }

    private static long WriteSnapshotInternal(PipeWriter body, CommandFeedService feed, long lastSent)
    {
        var frames = ArrayPool<FeedFrame>.Shared.Rent(FeedRingBuffer.CAPACITY);
        try
        {
            var count = feed.Snapshot(lastSent, frames.AsSpan(0, FeedRingBuffer.CAPACITY));
            for (var i = 0; i < count; i++)
            {
                body.Write(frames[i].Bytes);
                lastSent = frames[i].Id;
            }

            return lastSent;
        }
        finally
        {
            ArrayPool<FeedFrame>.Shared.Return(frames, clearArray: true);
        }
    }
}
