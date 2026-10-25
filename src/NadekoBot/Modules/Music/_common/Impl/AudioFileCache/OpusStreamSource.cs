namespace NadekoBot.Modules.Music;

public enum OpusReadResult
{
    Packet,
    Starved,
    End
}

// Reads the packets of a WebM Opus stream ahead of the voice send loop, so a slow read does not delay a frame.
public sealed class OpusStreamSource : IAsyncDisposable
{
    // About 12 seconds of YouTube Opus audio.
    private const int RING_BYTES = 1 << 18;

    // 0.5 seconds of 20 ms packets.
    private const int PREBUFFER_PACKETS = 25;
    private const int FULL_WAIT_MS = 100;

    private readonly Stream _stream;
    private readonly WebmOpusDemuxer _demuxer;
    private readonly int _frameSamples;
    private readonly OpusPacketRing _ring = new(RING_BYTES);
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _prebuffered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _producer = Task.CompletedTask;
    private volatile bool _completed;
    private int _disposed;

    private OpusStreamSource(Stream stream, WebmOpusDemuxer demuxer, int frameSamples)
    {
        _stream = stream;
        _demuxer = demuxer;
        _frameSamples = frameSamples;
    }

    // Null when the stream is not WebM Opus with frames of frameSamples. The source owns the stream.
    public static async Task<OpusStreamSource?> OpenAsync(Stream stream, int frameSamples, CancellationToken ct)
    {
        var demuxer = new WebmOpusDemuxer(stream);
        try
        {
            if (!await demuxer.InitializeAsync(ct))
            {
                demuxer.Dispose();
                return null;
            }

            var length = await demuxer.ReadPacketAsync(ct);
            if (length == 0 || OpusPacket.GetSamplesPerChannel(demuxer.Packet.Span) != frameSamples)
            {
                demuxer.Dispose();
                return null;
            }

            var source = new OpusStreamSource(stream, demuxer, frameSamples);
            source._ring.TryWrite(demuxer.Packet.Span);
            source._producer = Task.Run(source.ProduceInternalAsync, CancellationToken.None);
            return source;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Debug(ex, "Could not read the Opus stream");
            demuxer.Dispose();
            return null;
        }
        catch
        {
            demuxer.Dispose();
            throw;
        }
    }

    // Completes when half a second is ready, or the stream ended.
    public Task WaitBufferedAsync()
        => _prebuffered.Task;

    public OpusReadResult TryRead(byte[] output, out int length)
    {
        // Read the flag first: the producer sets it after its last write.
        var completed = _completed;
        length = _ring.TryRead(output);
        if (length > 0)
            return OpusReadResult.Packet;

        return completed ? OpusReadResult.End : OpusReadResult.Starved;
    }

    private async Task ProduceInternalAsync()
    {
        var ct = _cts.Token;
        try
        {
            var dropped = 0;
            while (await _demuxer.ReadPacketAsync(ct) > 0)
            {
                // The voice client advances the RTP timestamp by one frame for each packet.
                if (OpusPacket.GetSamplesPerChannel(_demuxer.Packet.Span) != _frameSamples)
                {
                    if (dropped++ == 0)
                        Log.Warning("The Opus stream has a packet of another length, so the bot drops it");

                    continue;
                }

                while (!_ring.TryWrite(_demuxer.Packet.Span))
                {
                    _prebuffered.TrySetResult();
                    await Task.Delay(FULL_WAIT_MS, ct);
                }

                if (_ring.Count >= PREBUFFER_PACKETS)
                    _prebuffered.TrySetResult();
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
            Log.Debug("The Opus stream ended inside a block");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading the Opus stream failed: {ErrorMessage}", ex.Message);
        }
        finally
        {
            _completed = true;
            _prebuffered.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _cts.CancelAsync();

        // Closing the stream ends a read which waits for the network.
        await _stream.DisposeAsync();
        try
        {
            await _producer;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "The Opus stream reader stopped with an error");
        }

        _demuxer.Dispose();
        _ring.Dispose();
        _cts.Dispose();
    }
}
