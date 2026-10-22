using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace NadekoBot.Hosted;

// Keeps the recent console lines for GET /api/logs.
public sealed class LogStreamService : IDisposable
{
    public const int BUFFER_CAPACITY = 5_000;
    public const int DEFAULT_TAIL = 500;
    // A line can encode to about 160 KB, so the history also has a size limit.
    public const long MAX_BUFFER_BYTES = 8 * 1024 * 1024;
    public const int MAX_SUBSCRIBERS = 16;
    public const int MAX_TEXT_LENGTH = 16_000;
    public const string REDACTED = "[redacted]";
    public const int MAX_TEMPLATE_LENGTH = 2_000;
    public const int MAX_PROPS = 32;
    public const int MAX_PROP_LENGTH = 1_000;
    public const int MAX_PROP_NAME_LENGTH = 100;
    public const int MAX_PROPS_CHARS = 8_000;

    // The shard of the line, which the console text does not show.
    private const string LOG_SOURCE_PROPERTY = "LogSource";

    private const int QUEUE_CAPACITY = 1024;
    private const int SUBSCRIBER_CAPACITY = 512;
    // The console template without the time, the level and the shard, which is always 0 in standalone mode.
    private const string MESSAGE_TEMPLATE = "{Message:lj}";

    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    private static readonly string[] _levelNames = ["verbose", "debug", "info", "warning", "error", "fatal"];
    private static readonly byte[] _dataPrefix = "data: "u8.ToArray();
    private static readonly byte[] _frameEnd = "\n\n"u8.ToArray();

    private const int MIN_EXTRA_SECRET_LENGTH = 6;

    private readonly SseHub _hub = new(BUFFER_CAPACITY, MAX_SUBSCRIBERS, SUBSCRIBER_CAPACITY, MAX_BUFFER_BYTES);
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

    public int Snapshot(long sinceId, Span<FeedFrame> destination)
        => _hub.Snapshot(sinceId, destination);

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

    // Adds one more value to hide, such as a proxy password. It does not release the queue.
    public void AddSecret(string? secret)
    {
        // A very short value would hide common words in every line.
        if (secret is not { Length: >= MIN_EXTRA_SECRET_LENGTH })
            return;

        lock (_secretsLock)
            Volatile.Write(ref _secrets, AddSecretInternal(_secrets, secret));
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

        var renderer = new LineRenderer();
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
                var line = renderer.Render(logEvent, Volatile.Read(ref _secrets));
                _hub.Add(EncodeInternal(++_nextId, line, output, json));
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _dropped);
            }
        }
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

    // Only the queue processor uses it, so its buffers and caches need no lock.
    private sealed class LineRenderer
    {
        private const int TEMPLATE_CACHE_LIMIT = 1024;

        private readonly MessageTemplateTextFormatter _formatter = new(MESSAGE_TEMPLATE, CultureInfo.InvariantCulture);
        private readonly StringWriter _writer = new(CultureInfo.InvariantCulture);

        // Serilog reuses the parsed template of a call site, so its tokens are stable keys.
        // Each event holds one token and gets its property only while it renders.
        private readonly Dictionary<PropertyToken, LogEvent> _tokenEvents = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, LogEvent> _plainEvents = new(StringComparer.Ordinal);

        public LogLineDto Render(LogEvent logEvent, string[] secrets)
        {
            var (text, exceptionAt) = RenderTextInternal(logEvent, secrets);
            return new LogLineDto(
                logEvent.Timestamp.UtcDateTime,
                LevelNameInternal(logEvent.Level),
                text,
                RenderTemplateInternal(logEvent.MessageTemplate.Text, secrets),
                RenderPropsInternal(logEvent, secrets),
                exceptionAt);
        }

        private (string Text, int? ExceptionAt) RenderTextInternal(LogEvent logEvent, string[] secrets)
        {
            var sb = _writer.GetStringBuilder();
            sb.Clear();
            _formatter.Format(logEvent, _writer);
            CleanInternal(sb, 0, secrets);

            // The message and the exception are cleaned apart, so the index of the separator is exact.
            var messageLength = sb.Length;
            if (logEvent.Exception is { } ex)
            {
                sb.Append('\n').Append(ex);
                CleanInternal(sb, messageLength, secrets);
            }

            // Cut after the redaction, so a cut cannot leave part of a secret.
            var end = Math.Min(sb.Length, MAX_TEXT_LENGTH);
            while (end > 0 && char.IsWhiteSpace(sb[end - 1]))
                end--;

            int? exceptionAt = logEvent.Exception is not null && messageLength < end ? messageLength : null;
            return (sb.ToString(0, end), exceptionAt);
        }

        private static string? RenderTemplateInternal(string template, string[] secrets)
        {
            template = RedactInternal(template, secrets);
            return template.Length > MAX_TEMPLATE_LENGTH ? null : template;
        }

        private Dictionary<string, string>? RenderPropsInternal(LogEvent logEvent, string[] secrets)
        {
            Dictionary<string, string>? props = null;
            var budget = MAX_PROPS_CHARS;
            foreach (var (name, value) in logEvent.Properties)
            {
                // A template from another source can make any name, so a name is a limited and checked field too.
                if (name == LOG_SOURCE_PROPERTY || name.Length > MAX_PROP_NAME_LENGTH || ContainsSecretInternal(name, secrets))
                    continue;

                props ??= new(Math.Min(logEvent.Properties.Count, MAX_PROPS), StringComparer.Ordinal);
                if (props.Count == MAX_PROPS)
                    break;

                string rendered;
                try
                {
                    rendered = RenderValueInternal(logEvent, name, value, secrets);
                }
                catch (Exception)
                {
                    // One value which cannot render must not drop the line.
                    continue;
                }

                // Each stream keeps frames, not bytes, so one line may not carry many long values.
                if (rendered.Length > budget)
                    break;

                budget -= rendered.Length;
                props[name] = rendered;
            }

            return props is { Count: > 0 } ? props : null;
        }

        private string RenderValueInternal(LogEvent logEvent, string name, LogEventPropertyValue value, string[] secrets)
        {
            var token = FindTokenInternal(logEvent.MessageTemplate, name);
            string rendered;
            if (value is ScalarValue { Value: string s } && token is null or { Format: null, Alignment: null })
            {
                rendered = s.Contains('\r') ? s.Replace("\r\n", "\n") : s;
            }
            else
            {
                // The single token goes through the same formatter as the message, so its format and alignment apply.
                var single = SingleTokenEventInternal(token, name);
                single.AddOrUpdateProperty(new LogEventProperty(name, value));
                try
                {
                    var sb = _writer.GetStringBuilder();
                    sb.Clear();
                    _formatter.Format(single, _writer);
                    sb.Replace("\r\n", "\n");
                    rendered = sb.ToString();
                }
                finally
                {
                    single.RemovePropertyIfPresent(name);
                }
            }

            rendered = RedactInternal(rendered, secrets);
            return rendered.Length <= MAX_PROP_LENGTH ? rendered : rendered[..CutIndexInternal(rendered, MAX_PROP_LENGTH)];
        }

        // The cut keeps a surrogate pair and a redaction marker whole.
        private static int CutIndexInternal(string value, int max)
        {
            var cut = char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
            for (var i = Math.Max(0, cut - REDACTED.Length + 1); i < cut; i++)
            {
                if (value.AsSpan(i).StartsWith(REDACTED, StringComparison.Ordinal))
                    return i;
            }

            return cut;
        }

        private LogEvent SingleTokenEventInternal(PropertyToken? token, string name)
        {
            if (token is not null)
            {
                if (!_tokenEvents.TryGetValue(token, out var single))
                {
                    if (_tokenEvents.Count >= TEMPLATE_CACHE_LIMIT)
                        _tokenEvents.Clear();

                    single = EmptyEventInternal(new MessageTemplate(token.ToString(), [token]));
                    _tokenEvents[token] = single;
                }

                return single;
            }

            if (!_plainEvents.TryGetValue(name, out var plain))
            {
                if (_plainEvents.Count >= TEMPLATE_CACHE_LIMIT)
                    _plainEvents.Clear();

                var raw = "{" + name + "}";
                plain = EmptyEventInternal(new MessageTemplate(raw, [new PropertyToken(name, raw)]));
                _plainEvents[name] = plain;
            }

            return plain;
        }

        private static LogEvent EmptyEventInternal(MessageTemplate template)
            => new(DateTimeOffset.UnixEpoch, LogEventLevel.Information, null, template, []);

        private static bool ContainsSecretInternal(string value, string[] secrets)
        {
            foreach (var secret in secrets)
            {
                if (value.Contains(secret, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
        // A property which the template uses twice keeps the text of its first token.
        private static PropertyToken? FindTokenInternal(MessageTemplate template, string name)
        {
            foreach (var token in template.Tokens)
            {
                if (token is PropertyToken pt && pt.PropertyName == name)
                    return pt;
            }

            return null;
        }

        private static void CleanInternal(StringBuilder sb, int start, string[] secrets)
        {
            sb.Replace("\r\n", "\n", start, sb.Length - start);
            foreach (var secret in secrets)
                sb.Replace(secret, REDACTED, start, sb.Length - start);
        }

        private static string RedactInternal(string value, string[] secrets)
        {
            if (value.Contains('\r'))
                value = value.Replace("\r\n", "\n");

            foreach (var secret in secrets)
            {
                if (value.Contains(secret, StringComparison.Ordinal))
                    value = value.Replace(secret, REDACTED, StringComparison.Ordinal);
            }

            return value;
        }
    }
}
