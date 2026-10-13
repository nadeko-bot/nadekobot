using System.Buffers;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;

namespace NadekoBot.Hosted.Endpoints;

public static class SseResponse
{
    public const string EVENT_STREAM_CONTENT_TYPE = "text/event-stream";

    private const string NO_STORE = "no-store";

    // Tells nginx-style proxies to pass each frame on at once.
    private const string PROXY_BUFFERING_HEADER = "X-Accel-Buffering";
    private const string PROXY_BUFFERING_OFF = "no";

    public static void SetHeaders(HttpResponse response)
    {
        response.Headers.ContentType = EVENT_STREAM_CONTENT_TYPE;
        response.Headers.CacheControl = NO_STORE;
        response.Headers[PROXY_BUFFERING_HEADER] = PROXY_BUFFERING_OFF;
    }

    // A frame which the snapshot already sent arrives again on the channel; its id is not above lastSent.
    public static async Task PumpAsync(
        PipeWriter body,
        ChannelReader<FeedFrame> reader,
        long lastSent,
        CancellationToken ct)
    {
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var frame))
                {
                    if (frame.IsKeepAlive)
                    {
                        body.Write(frame.Bytes);
                        continue;
                    }

                    if (frame.Id <= lastSent)
                        continue;

                    body.Write(frame.Bytes);
                    lastSent = frame.Id;
                }

                await body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
