using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace NadekoBot.Hosted;

// Keeps the recent console lines for GET /api/logs.
public sealed class LogStreamService : IDisposable
{
    public const int BUFFER_CAPACITY = 500;
    public const int MAX_SUBSCRIBERS = 16;
    public const int MAX_TEXT_LENGTH = 16_000;
    public const string REDACTED = "[redacted]";

    private const int QUEUE_CAPACITY = 1024;
    private const int SUBSCRIBER_CAPACITY = 512;
    // The console template without the time, the level and the shard, which is always 0 in standalone mode.
    private const string MESSAGE_TEMPLATE = "{Message:lj}";

    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    private static readonly string[] _levelNames = ["verbose", "debug", "info", "warning", "error", "fatal"];
    private static readonly byte[] _dataPrefix = "data: "u8.ToArray();
    private static readonly byte[] _frameEnd = "\n\n"u8.ToArray();

    private readonly SseHub _hub = new(BUFFER_CAPACITY, MAX_SUBSCRIBERS, SUBSCRIBER_CAPACITY);
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    // A null item is a keepalive tick, so the processor stays the only writer of the stream channels.
    private readonly Channel<LogEvent?> _queue = Channel.CreateBounded<LogEvent?>(
        new BoundedChannelOptions(QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });
    private readonly TaskCompletionSource _secretsKnown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _secretsLock = new();
    private string[] _secrets = [];
    private int _dropped;
    private int _disposed;
    private long _nextId;

    public LogStreamService(TimeProvider time)
    {
        _time = time;
        Sink = new QueueSink(this);
        _ = Task.Run(ProcessQueueInternalAsync);
        _ = Task.Run(KeepAliveLoopInternalAsync);
    }

    // Not IDisposable, so a logger which closes cannot stop the stream.
    public ILogEventSink Sink { get; }

    public FeedSubscription? Subscribe()
        => _hub.Subscribe();

    public int Tail(int count, Span<FeedFrame> destination, out long newestId)
        => _hub.Tail(count, destination, out newestId);

    // The processor renders no line before the first call, so no line leaves before the bot knows what to hide.
    public void AddSecrets(string? token, string? apiKey)
    {
        lock (_secretsLock)
        {
            var secrets = AddSecretInternal(_secrets, token);
            Volatile.Write(ref _secrets, AddSecretInternal(secrets, apiKey));
        }

        _secretsKnown.TrySetResult();
    }

    private static string[] AddSecretInternal(string[] secrets, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || Array.IndexOf(secrets, secret) >= 0)
            return secrets;

        return [..secrets, secret];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _queue.Writer.TryComplete();
        _stop.Cancel();
        _hub.CompleteAll();
    }

    private void EnqueueInternal(LogEvent logEvent)
    {
        // Counted, not logged here: the warning would come back into this queue.
        if (!_queue.Writer.TryWrite(logEvent))
            Interlocked.Increment(ref _dropped);
    }

    private async Task ProcessQueueInternalAsync()
    {
        try
        {
            await _secretsKnown.Task.WaitAsync(_stop.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var formatter = new MessageTemplateTextFormatter(MESSAGE_TEMPLATE, CultureInfo.InvariantCulture);
        var text = new StringWriter(CultureInfo.InvariantCulture);
        var output = new ArrayBufferWriter<byte>();
        await using var json = new Utf8JsonWriter(output);

        await foreach (var logEvent in _queue.Reader.ReadAllAsync())
        {
            if (logEvent is null)
            {
                _hub.PublishKeepAlive();
                continue;
            }

            try
            {
                var line = new LogLineDto(
                    logEvent.Timestamp.UtcDateTime,
                    LevelNameInternal(logEvent.Level),
                    RenderInternal(logEvent, formatter, text, Volatile.Read(ref _secrets)));
                _hub.Add(EncodeInternal(++_nextId, line, output, json));
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _dropped);
            }
        }
    }

    private static string RenderInternal(
        LogEvent logEvent,
        MessageTemplateTextFormatter formatter,
        StringWriter writer,
        string[] secrets)
    {
        var sb = writer.GetStringBuilder();
        sb.Clear();
        formatter.Format(logEvent, writer);
        if (logEvent.Exception is { } ex)
            sb.Append('\n').Append(ex);

        sb.Replace("\r\n", "\n");
        foreach (var secret in secrets)
            sb.Replace(secret, REDACTED);

        // Cut after the redaction, so a cut cannot leave part of a secret.
        var end = Math.Min(sb.Length, MAX_TEXT_LENGTH);
        while (end > 0 && char.IsWhiteSpace(sb[end - 1]))
            end--;

        return sb.ToString(0, end);
    }

    private static string LevelNameInternal(LogEventLevel level)
        => (uint)level < (uint)_levelNames.Length ? _levelNames[(int)level] : level.ToString();

    private static FeedFrame EncodeInternal(long id, LogLineDto line, ArrayBufferWriter<byte> output, Utf8JsonWriter json)
    {
        output.ResetWrittenCount();
        json.Reset(output);

        output.Write(_dataPrefix);
        JsonSerializer.Serialize(json, line, ApiJsonContext.Default.LogLineDto);
        json.Flush();
        output.Write(_frameEnd);

        return new FeedFrame(id, output.WrittenSpan.ToArray());
    }

    private async Task KeepAliveLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(KeepAliveInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                if (_hub.HasSubscribers)
                    _queue.Writer.TryWrite(null);

                // Before the secrets are known the queue only fills up; a warning each tick would add to it.
                if (_secretsKnown.Task.IsCompleted && Interlocked.Exchange(ref _dropped, 0) is var dropped and > 0)
                    Log.Warning("The console log stream dropped {Count} lines", dropped);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class QueueSink(LogStreamService owner) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
            => owner.EnqueueInternal(logEvent);
    }
}
