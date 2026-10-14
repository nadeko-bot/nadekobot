using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NadekoBot.Hosted;

public enum ActivityKind : byte
{
    Command,
    Error,
    Join,
    Leave
}

public sealed record ActivityReport(
    long FirstDay,
    long? FirstHour,
    ActivityCounts[] Days,
    long[] Hours,
    TopCommandDto[] TopCommands);

// Counts commands, command errors, joins and leaves for each guild and UTC hour, and writes them to data/activity.db.
public sealed class GuildActivityService
{
    public const int RETENTION_DAYS = 30;
    public const int TOP_COMMANDS = 5;

    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(5);

    private const int QUEUE_CAPACITY = 4096;
    private const long SECONDS_PER_HOUR = 3600;

    private readonly TimeProvider _time;
    private readonly ActivityStore? _store;
    private readonly CancellationTokenSource _stop = new();
    // Only the worker reads and writes the pending counts, the store and the dictionaries.
    private readonly Channel<WorkItem> _queue = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(QUEUE_CAPACITY)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    private readonly Dictionary<HourKey, ActivityCounts> _pendingHours = new();
    private readonly Dictionary<CommandKey, long> _pendingCommands = new();
    private readonly List<KeyValuePair<CommandKey, long>> _commandBuffer = new();
    private readonly List<HourKey> _expiredHours = new();
    private readonly List<CommandKey> _expiredCommands = new();
    private readonly Task _worker;
    private readonly Task _flushLoop;

    private ICommandHandler? _commandHandler;
    private BaseSocketClient? _client;
    private int _stopped;
    private int _dropped;

    public GuildActivityService(string dbPath, TimeProvider time)
    {
        _time = time;
        _store = ActivityStore.Open(dbPath);
        _worker = Task.Run(WorkerInternalAsync);
        _flushLoop = Task.Run(FlushLoopInternalAsync);
    }

    public void Subscribe(ICommandHandler commandHandler, BaseSocketClient client)
    {
        _commandHandler = commandHandler;
        _client = client;
        commandHandler.CommandExecuted += OnCommandExecuted;
        commandHandler.CommandErrored += OnCommandErrored;
        client.UserJoined += OnUserJoined;
        client.UserLeft += OnUserLeft;
    }

    // Never blocks: a full queue drops the event and counts it.
    public void Record(ActivityKind kind, ulong guildId, string? command = null)
    {
        var hour = _time.GetUtcNow().ToUnixTimeSeconds() / SECONDS_PER_HOUR;
        if (!_queue.Writer.TryWrite(new WorkItem(WorkKind.Count, kind, guildId, hour, command, null)))
            Interlocked.Increment(ref _dropped);
    }

    // Null after the service stopped.
    public async Task<ActivityReport?> ReadAsync(ulong guildId, int days, CancellationToken ct)
    {
        var request = new ReadRequest(guildId, days);
        try
        {
            await _queue.Writer.WriteAsync(new WorkItem(WorkKind.Read, default, guildId, 0, null, request), ct);
        }
        catch (ChannelClosedException)
        {
            return null;
        }

        return await request.Result.Task.WaitAsync(ct);
    }

    // Writes the pending counts. The worker ends after the queue is empty.
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            if (_commandHandler is { } handler)
            {
                handler.CommandExecuted -= OnCommandExecuted;
                handler.CommandErrored -= OnCommandErrored;
            }

            if (_client is { } client)
            {
                client.UserJoined -= OnUserJoined;
                client.UserLeft -= OnUserLeft;
            }

            _stop.Cancel();
            _queue.Writer.TryComplete();
        }

        await _flushLoop;
        await _worker;
        _stop.Dispose();
    }

    private Task OnCommandExecuted(IUserMessage msg, CommandInfo cmd)
    {
        if (msg.Channel is IGuildChannel channel)
            Record(ActivityKind.Command, channel.GuildId, cmd.Aliases.Count > 0 ? cmd.Aliases[0] : cmd.Name);

        return Task.CompletedTask;
    }

    private Task OnCommandErrored(CommandInfo cmd, ITextChannel? channel, string error)
    {
        if (channel is not null)
            Record(ActivityKind.Error, channel.GuildId);

        return Task.CompletedTask;
    }

    private Task OnUserJoined(SocketGuildUser user)
    {
        Record(ActivityKind.Join, user.Guild.Id);
        return Task.CompletedTask;
    }

    private Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        Record(ActivityKind.Leave, guild.Id);
        return Task.CompletedTask;
    }

    private async Task FlushLoopInternalAsync()
    {
        using var timer = new PeriodicTimer(FlushInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
                await _queue.Writer.WriteAsync(WorkItem.Flush, _stop.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private async Task WorkerInternalAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync())
        {
            try
            {
                switch (item.Work)
                {
                    case WorkKind.Count:
                        CountInternal(item);
                        break;
                    case WorkKind.Flush:
                        FlushInternal();
                        break;
                    case WorkKind.Read:
                        item.Read!.Result.TrySetResult(BuildReportInternal(item.Read));
                        break;
                }
            }
            catch (Exception ex)
            {
                if (item.Read is { } read)
                    read.Result.TrySetException(ex);
                else
                    Log.Warning(ex, "Error counting server activity");
            }
        }

        FlushInternal();
        _store?.Dispose();
    }

    private void CountInternal(in WorkItem item)
    {
        ref var counts = ref CollectionsMarshal.GetValueRefOrAddDefault(_pendingHours,
            new HourKey(item.GuildId, item.Hour),
            out _);

        switch (item.Kind)
        {
            case ActivityKind.Command:
                counts.Commands++;
                if (item.Command is { } name)
                {
                    ref var runs = ref CollectionsMarshal.GetValueRefOrAddDefault(_pendingCommands,
                        new CommandKey(item.GuildId, item.Hour / ActivityReportBuilder.HOURS_PER_DAY, name),
                        out _);
                    runs++;
                }

                break;
            case ActivityKind.Error:
                counts.Errors++;
                break;
            case ActivityKind.Join:
                counts.Joins++;
                break;
            case ActivityKind.Leave:
                counts.Leaves++;
                break;
        }
    }

    // The counts stay pending until a write commits them, so a failed write loses nothing but old days.
    private void FlushInternal()
    {
        if (Interlocked.Exchange(ref _dropped, 0) is var dropped and > 0)
            Log.Warning("The server activity queue was full. Dropped {Count} events", dropped);

        var firstKeptDay = TodayInternal() - (RETENTION_DAYS - 1);
        if (_store is null)
        {
            DropExpiredInternal(firstKeptDay);
            return;
        }

        if (_pendingHours.Count == 0 && _pendingCommands.Count == 0)
            return;

        // Within a guild and day the most used commands go first, so the cap keeps them.
        _commandBuffer.Clear();
        _commandBuffer.AddRange(_pendingCommands);
        _commandBuffer.Sort(static (a, b) =>
        {
            var c = a.Key.GuildId.CompareTo(b.Key.GuildId);
            if (c != 0)
                return c;

            c = a.Key.Day.CompareTo(b.Key.Day);
            return c != 0 ? c : b.Value.CompareTo(a.Value);
        });

        try
        {
            _store.Write(_pendingHours, _commandBuffer, firstKeptDay);
            _pendingHours.Clear();
            _pendingCommands.Clear();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Cannot write the server activity. The bot tries again at the next write");
            DropExpiredInternal(firstKeptDay);
        }
        finally
        {
            _commandBuffer.Clear();
        }
    }

    private void DropExpiredInternal(long firstKeptDay)
    {
        var firstKeptHour = firstKeptDay * ActivityReportBuilder.HOURS_PER_DAY;
        foreach (var (key, _) in _pendingHours)
        {
            if (key.Hour < firstKeptHour)
                _expiredHours.Add(key);
        }

        foreach (var (key, _) in _pendingCommands)
        {
            if (key.Day < firstKeptDay)
                _expiredCommands.Add(key);
        }

        foreach (var key in _expiredHours)
            _pendingHours.Remove(key);

        foreach (var key in _expiredCommands)
            _pendingCommands.Remove(key);

        _expiredHours.Clear();
        _expiredCommands.Clear();
    }

    private ActivityReport BuildReportInternal(ReadRequest request)
    {
        var today = TodayInternal();
        var firstDay = today - (request.Days - 1);
        var builder = new ActivityReportBuilder(firstDay, request.Days, today - (RETENTION_DAYS - 1));

        _store?.Read(request.GuildId, builder);

        foreach (var (key, counts) in _pendingHours)
        {
            if (key.GuildId == request.GuildId)
                builder.AddHour(key.Hour, counts);
        }

        foreach (var (key, runs) in _pendingCommands)
        {
            if (key.GuildId == request.GuildId && key.Day >= firstDay)
                builder.AddCommand(key.Name, runs);
        }

        return new ActivityReport(firstDay, builder.FirstHour, builder.Days, builder.Hours, TopInternal(builder.Commands));
    }

    // Most runs first; equal runs by name, so the answer does not change between calls.
    private static TopCommandDto[] TopInternal(Dictionary<string, long> commands)
    {
        if (commands.Count == 0)
            return [];

        var top = new KeyValuePair<string, long>[Math.Min(TOP_COMMANDS, commands.Count)];
        var count = 0;
        foreach (var entry in commands)
        {
            if (count == top.Length && !IsBetterInternal(entry, top[count - 1]))
                continue;

            var i = count < top.Length ? count++ : count - 1;
            while (i > 0 && IsBetterInternal(entry, top[i - 1]))
            {
                top[i] = top[i - 1];
                i--;
            }

            top[i] = entry;
        }

        var result = new TopCommandDto[count];
        for (var i = 0; i < count; i++)
            result[i] = new TopCommandDto(top[i].Key, top[i].Value);

        return result;
    }

    private static bool IsBetterInternal(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
        => a.Value > b.Value || (a.Value == b.Value && string.CompareOrdinal(a.Key, b.Key) < 0);

    private long TodayInternal()
        => _time.GetUtcNow().ToUnixTimeSeconds() / SECONDS_PER_HOUR / ActivityReportBuilder.HOURS_PER_DAY;

    private enum WorkKind : byte
    {
        Count,
        Flush,
        Read
    }

    private sealed class ReadRequest(ulong guildId, int days)
    {
        public ulong GuildId { get; } = guildId;
        public int Days { get; } = days;
        public TaskCompletionSource<ActivityReport> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly record struct WorkItem(
        WorkKind Work,
        ActivityKind Kind,
        ulong GuildId,
        long Hour,
        string? Command,
        ReadRequest? Read)
    {
        public static readonly WorkItem Flush = new(WorkKind.Flush, default, 0, 0, null, null);
    }
}
