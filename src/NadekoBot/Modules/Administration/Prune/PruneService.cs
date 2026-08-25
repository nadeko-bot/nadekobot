using System.Net;

namespace NadekoBot.Modules.Administration.Services;

public sealed class PruneService(ILogCommandService logService) : INService
{
    private const int MAX_EXTRA_SCAN = 1000;

    // messages older than 14 days can't be bulk deleted, the margin prevents a message
    // from crossing that line in the middle of a long running prune
    private static readonly TimeSpan _bulkDeleteMaxAge = TimeSpan.FromDays(14) - TimeSpan.FromHours(1);
    private static readonly TimeSpan _batchDelay = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<ulong, PruneSession> _activePrunes = new();

    public static ulong GetPruneKey(IMessageChannel channel)
        => (channel as ITextChannel)?.GuildId ?? channel.Id;

    /// <summary>
    /// Reserves the prune slot for the specified channel's guild.
    /// The returned session must be disposed to release the slot.
    /// </summary>
    /// <returns>The session, or null if a prune is already running.</returns>
    public PruneSession? TryStart(IMessageChannel channel)
    {
        var key = GetPruneKey(channel);
        var session = new PruneSession(key, _activePrunes);

        if (_activePrunes.TryAdd(key, session))
            return session;

        session.Dispose();
        return null;
    }

    public bool Cancel(ulong key)
        => _activePrunes.TryGetValue(key, out var session) && session.Cancel();

    public async Task<PruneResult> PruneWhere(
        PruneSession session,
        IMessageChannel channel,
        int amount,
        Func<IMessage, bool> predicate,
        IProgress<(int deleted, int total)> progress,
        ulong? after = null,
        DateTimeOffset? notOlderThan = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        try
        {
            await PruneInternalAsync(channel,
                amount,
                predicate,
                progress,
                after,
                notOlderThan,
                session.Token);

            return PruneResult.Success;
        }
        catch (OperationCanceledException)
        {
            return PruneResult.Cancelled;
        }
        catch (HttpException ex)
        {
            Log.Warning(ex, "Prune failed in channel {ChannelId}: {ErrorMessage}", channel.Id, ex.Message);
            return PruneResult.Error;
        }
    }

    private async Task PruneInternalAsync(
        IMessageChannel channel,
        int amount,
        Func<IMessage, bool> predicate,
        IProgress<(int deleted, int total)> progress,
        ulong? after,
        DateTimeOffset? notOlderThan,
        CancellationToken token)
    {
        var opts = new RequestOptions
        {
            CancelToken = token
        };

        var bulkChannel = channel as ITextChannel;
        var bulkFloor = DateTimeOffset.UtcNow - _bulkDeleteMaxAge;
        var scanLimit = amount + MAX_EXTRA_SCAN;

        var bulkDeletable = new List<IMessage>();
        var singleDeletable = new List<IMessage>();

        var deleted = 0;
        var scanned = 0;
        ulong? cursor = null;

        while (deleted < amount && scanned < scanLimit)
        {
            var batchSize = Math.Min(DiscordConfig.MaxMessagesPerBatch, scanLimit - scanned);

            var page = cursor is ulong c
                ? await channel.GetMessagesAsync(c, Direction.Before, batchSize, options: opts).FlattenAsync()
                : await channel.GetMessagesAsync(batchSize, options: opts).FlattenAsync();

            bulkDeletable.Clear();
            singleDeletable.Clear();

            var remaining = amount - deleted;
            var oldestId = ulong.MaxValue;
            var pageCount = 0;
            var reachedFloor = false;

            foreach (var msg in page)
            {
                pageCount++;

                if (msg.Id < oldestId)
                    oldestId = msg.Id;

                if (notOlderThan is DateTimeOffset floor && msg.CreatedAt < floor)
                {
                    reachedFloor = true;
                    continue;
                }

                if (after is ulong afterId && msg.Id <= afterId)
                    continue;

                if (bulkDeletable.Count + singleDeletable.Count >= remaining)
                    continue;

                if (!predicate(msg))
                    continue;

                logService.AddDeleteIgnore(msg.Id);

                if (bulkChannel is not null && msg.CreatedAt > bulkFloor)
                    bulkDeletable.Add(msg);
                else
                    singleDeletable.Add(msg);
            }

            if (bulkDeletable.Count > 0)
            {
                deleted += await DeleteBatchInternalAsync(bulkChannel!, bulkDeletable, opts);
                progress.Report((deleted, amount));
            }

            foreach (var msg in singleDeletable)
            {
                token.ThrowIfCancellationRequested();

                if (await DeleteOneInternalAsync(msg, opts))
                    deleted++;

                progress.Report((deleted, amount));
            }

            scanned += pageCount;

            if (pageCount < batchSize || reachedFloor)
                return;

            if (after is ulong lastWanted && oldestId <= lastWanted)
                return;

            cursor = oldestId;

            if (deleted < amount && scanned < scanLimit)
                await Task.Delay(_batchDelay, token);
        }
    }

    private static async Task<int> DeleteBatchInternalAsync(
        ITextChannel channel,
        List<IMessage> msgs,
        RequestOptions opts)
    {
        if (msgs.Count == 1)
            return await DeleteOneInternalAsync(msgs[0], opts) ? 1 : 0;

        try
        {
            await channel.DeleteMessagesAsync(msgs, opts);
            return msgs.Count;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            // a message in the batch is gone or too old to bulk delete
            Log.Warning("Bulk delete failed in channel {ChannelId}, deleting messages one by one", channel.Id);
        }

        var deleted = 0;
        foreach (var msg in msgs)
        {
            if (await DeleteOneInternalAsync(msg, opts))
                deleted++;
        }

        return deleted;
    }

    private static async Task<bool> DeleteOneInternalAsync(IMessage msg, RequestOptions opts)
    {
        try
        {
            await msg.DeleteAsync(opts);
            return true;
        }
        catch (HttpException ex) when (ex.HttpCode is HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}

public sealed class PruneSession : IDisposable
{
    private readonly ulong _key;
    private readonly ConcurrentDictionary<ulong, PruneSession> _owner;
    private readonly CancellationTokenSource _cancelSource = new();

    public PruneSession(ulong key, ConcurrentDictionary<ulong, PruneSession> owner)
    {
        _key = key;
        _owner = owner;
    }

    public CancellationToken Token
        => _cancelSource.Token;

    public bool Cancel()
    {
        try
        {
            _cancelSource.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _owner.TryRemove(_key, out _);
        _cancelSource.Dispose();
    }
}
