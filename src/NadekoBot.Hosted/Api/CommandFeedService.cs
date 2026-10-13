using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace NadekoBot.Hosted;

public sealed class CommandFeedService : IDisposable
{
    public const int MAX_SUBSCRIBERS = 16;
    public const string EXEC_EVENT = "exec";
    public const string ERROR_EVENT = "error";

    private const int QUEUE_CAPACITY = 1024;
    private const int SUBSCRIBER_CAPACITY = 64;
    private const int MESSAGE_PREVIEW_LENGTH = 200;
    private const string NO_USER_ID = "0";
    private const string NO_USERNAME = "-";
    private const int MAX_INT64_DIGITS = 20;

    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(5);

    private static readonly byte[] _idPrefix = "id: "u8.ToArray();
    private static readonly byte[] _dataPrefix = "\ndata: "u8.ToArray();
    private static readonly byte[] _frameEnd = "\n\n"u8.ToArray();

    private readonly ICommandHandler _commandHandler;
    private readonly SseHub _hub = new(FeedRingBuffer.CAPACITY, MAX_SUBSCRIBERS, SUBSCRIBER_CAPACITY);
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    // A keepalive tick goes through the queue too, so the processor stays the only writer of the subscriber channels.
    private readonly Channel<FeedItem> _queue = Channel.CreateBounded<FeedItem>(
        new BoundedChannelOptions(QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    private int _disposed;
    private int _dropped;
    private long _nextEventId;

    public CommandFeedService(ICommandHandler commandHandler, TimeProvider time)
    {
        _commandHandler = commandHandler;
        _time = time;
        // Ids start at the boot time in Unix milliseconds, so the last id a client saw stays valid after a restart.
        _nextEventId = time.GetUtcNow().ToUnixTimeMilliseconds();

        commandHandler.CommandExecuted += OnCommandExecuted;
        commandHandler.CommandErrored += OnCommandErrored;
        _ = Task.Run(ProcessQueueInternalAsync);
        _ = Task.Run(KeepAliveLoopInternalAsync);
    }

    public int Snapshot(long sinceId, Span<FeedFrame> destination)
        => _hub.Snapshot(sinceId, destination);

    public FeedSubscription? Subscribe()
        => _hub.Subscribe();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _commandHandler.CommandExecuted -= OnCommandExecuted;
        _commandHandler.CommandErrored -= OnCommandErrored;
        _queue.Writer.TryComplete();
        _stop.Cancel();
        _hub.CompleteAll();
    }

    // Handlers run on the command path, so they only queue references; the processor builds the strings.
    private Task OnCommandExecuted(IUserMessage msg, CommandInfo cmd)
    {
        EnqueueInternal(new FeedItem(_time.GetUtcNow().UtcDateTime, cmd, msg, msg.Channel as ITextChannel, null));
        return Task.CompletedTask;
    }

    private Task OnCommandErrored(CommandInfo cmd, ITextChannel? channel, string error)
    {
        EnqueueInternal(new FeedItem(_time.GetUtcNow().UtcDateTime, cmd, null, channel, error));
        return Task.CompletedTask;
    }

    private void EnqueueInternal(in FeedItem item)
    {
        // Counted, not logged here: a burst would write one line per command.
        if (!_queue.Writer.TryWrite(item))
            Interlocked.Increment(ref _dropped);
    }

    private static FeedEventDto ToDtoInternal(in FeedItem item, CommandInfo cmd, long id)
    {
        if (item.Message is { } msg)
        {
            return new FeedEventDto(
                id,
                EXEC_EVENT,
                item.Timestamp,
                item.Channel?.GuildId.ToString(),
                msg.Channel.Id.ToString(),
                msg.Author.Id.ToString(),
                msg.Author.Username,
                CommandNameInternal(cmd),
                ModuleNameInternal(cmd),
                msg.Content.TrimTo(MESSAGE_PREVIEW_LENGTH) ?? string.Empty,
                null);
        }

        return new FeedEventDto(
            id,
            ERROR_EVENT,
            item.Timestamp,
            item.Channel?.GuildId.ToString(),
            item.Channel?.Id.ToString(),
            NO_USER_ID,
            NO_USERNAME,
            CommandNameInternal(cmd),
            ModuleNameInternal(cmd),
            string.Empty,
            item.Error);
    }

    // The only writer of ids, the ring buffer and the subscriber channels, so every subscriber sees ids in order.
    private async Task ProcessQueueInternalAsync()
    {
        var output = new ArrayBufferWriter<byte>();
        using var json = new Utf8JsonWriter(output);

        await foreach (var item in _queue.Reader.ReadAllAsync())
        {
            try
            {
                if (item.Command is not { } cmd)
                {
                    _hub.PublishKeepAlive();
                    continue;
                }

                var frame = EncodeInternal(ToDtoInternal(item, cmd, _nextEventId), output, json);
                _nextEventId++;
                _hub.Add(frame);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error publishing a command feed event");
            }
        }
    }

    private async Task KeepAliveLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(KeepAliveInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                if (_hub.HasSubscribers)
                    _queue.Writer.TryWrite(FeedItem.KeepAlive);

                if (Interlocked.Exchange(ref _dropped, 0) is var dropped and > 0)
                    Log.Warning("The command feed queue was full. Dropped {Count} events", dropped);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private static FeedFrame EncodeInternal(FeedEventDto dto, ArrayBufferWriter<byte> output, Utf8JsonWriter json)
    {
        output.ResetWrittenCount();
        json.Reset(output);

        output.Write(_idPrefix);
        Utf8Formatter.TryFormat(dto.Id, output.GetSpan(MAX_INT64_DIGITS), out var written);
        output.Advance(written);
        output.Write(_dataPrefix);

        JsonSerializer.Serialize(json, dto, ApiJsonContext.Default.FeedEventDto);
        json.Flush();

        output.Write(_frameEnd);
        return new FeedFrame(dto.Id, output.WrittenSpan.ToArray());
    }

    private static string CommandNameInternal(CommandInfo cmd)
        => cmd.Aliases.Count > 0 ? cmd.Aliases[0] : cmd.Name;

    private static string ModuleNameInternal(CommandInfo cmd)
        => cmd.Module.GetTopLevelModule().Name;
}

// A command without a message is an error event; no command at all is a keepalive tick.
public readonly record struct FeedItem(
    DateTime Timestamp,
    CommandInfo? Command,
    IUserMessage? Message,
    ITextChannel? Channel,
    string? Error)
{
    public static readonly FeedItem KeepAlive = default;
}
