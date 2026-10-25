using System.Net;
using System.Net.Http.Headers;

namespace NadekoBot.Modules.Music;

// The audio body of the music service. When the connection breaks, it asks again from the last byte, as ffmpeg does
// with -reconnect.
public sealed class ServiceAudioStream : Stream
{
    private const int MAX_RECONNECTS = 3;
    private const int RECONNECT_DELAY_MS = 500;
    private const int READ_STALL_MS = 30_000;
    private const int CONNECT_TIMEOUT_MS = 30_000;

    private static readonly Action<object?> _cancelStall = static s => ((CancellationTokenSource)s!).Cancel();

    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _key;
    private HttpResponseMessage? _response;
    private Stream? _body;
    private CancellationTokenSource _stall = new();
    private long _position;
    private long? _length;
    private int _reconnects;
    private bool _disposed;

    private ServiceAudioStream(HttpClient http, string url, string key)
    {
        // The body is read for the whole song, so only the connect and each read have a time limit.
        http.Timeout = Timeout.InfiniteTimeSpan;
        _http = http;
        _url = url;
        _key = key;
    }

    // Null when the service refuses, or the audio is not WebM, so that ffmpeg plays it.
    public static async Task<ServiceAudioStream?> OpenAsync(HttpClient http, string url, string key, CancellationToken ct)
    {
        var stream = new ServiceAudioStream(http, url, key);
        try
        {
            if (await stream.ConnectInternalAsync(true, ct))
                return stream;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            Log.Debug(ex, "The music service audio request failed");
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        await stream.DisposeAsync();
        return null;
    }

    private async Task<bool> ConnectInternalAsync(bool first, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _url);
        req.Headers.Authorization = new("Bearer", _key);
        if (_position > 0)
            req.Headers.Range = new RangeHeaderValue(_position, null);

        HttpResponseMessage res;
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(CONNECT_TIMEOUT_MS);
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, connect.Token);
        }

        var headers = res.Content.Headers;
        var valid = _position == 0
            ? res.StatusCode == HttpStatusCode.OK
              || (res.StatusCode == HttpStatusCode.PartialContent && headers.ContentRange?.From is null or 0)
            : res.StatusCode == HttpStatusCode.PartialContent && headers.ContentRange?.From == _position;

        if (valid && first && headers.ContentType?.MediaType is { } type
            && !string.Equals(type, "audio/webm", StringComparison.OrdinalIgnoreCase))
        {
            Log.Debug("The music service sent {ContentType} audio, so ffmpeg plays it", type);
            valid = false;
        }

        if (!valid)
        {
            if (!first || res.StatusCode != HttpStatusCode.OK)
                Log.Warning("The music service audio answered {StatusCode}", (int)res.StatusCode);

            res.Dispose();
            return false;
        }

        if (first)
            _length = headers.ContentRange?.Length ?? headers.ContentLength;

        _response = res;
        _body = await res.Content.ReadAsStreamAsync(ct);
        return true;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            if (_body is { } body)
            {
                try
                {
                    int read;
                    using (ct.UnsafeRegister(_cancelStall, _stall))
                    {
                        _stall.CancelAfter(READ_STALL_MS);
                        try
                        {
                            read = await body.ReadAsync(buffer, _stall.Token);
                        }
                        finally
                        {
                            _stall.CancelAfter(Timeout.Infinite);
                        }
                    }

                    // The timer can fire after the read finished, and the next read must not get a cancelled token.
                    if (_stall.IsCancellationRequested && !ct.IsCancellationRequested && !_stall.TryReset())
                        ReplaceStallInternal();

                    _position += read;
                    if (read > 0 || _length is not { } length || _position >= length)
                        return read;

                    Log.Debug("The music service audio ended at {Position} of {Length} bytes", _position, length);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested
                                           && ex is IOException or HttpRequestException or OperationCanceledException)
                {
                    Log.Debug(ex, "The music service audio connection broke at {Position} bytes", _position);
                }
            }

            ct.ThrowIfCancellationRequested();

            // A connection which breaks after the last byte has lost nothing.
            if (_length is { } total && _position >= total)
                return 0;

            if (++_reconnects > MAX_RECONNECTS)
                throw new IOException("The music service audio connection broke too many times");

            CloseBodyInternal();
            await Task.Delay(RECONNECT_DELAY_MS, ct);
            if (!await ConnectInternalAsync(false, ct))
                throw new IOException("The music service did not resume the audio");
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    private void CloseBodyInternal()
    {
        _body?.Dispose();
        _body = null;
        _response?.Dispose();
        _response = null;

        if (!_stall.TryReset())
            ReplaceStallInternal();
    }

    private void ReplaceStallInternal()
    {
        _stall.Dispose();
        _stall = new();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _body?.Dispose();
            _response?.Dispose();
            _stall.Dispose();
            _http.Dispose();
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
}
