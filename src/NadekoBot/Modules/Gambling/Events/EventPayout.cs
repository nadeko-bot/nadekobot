using NadekoBot.Services.Currency;

namespace NadekoBot.Modules.Gambling.Common.Events;

public sealed class EventPayout(ICurrencyService cs, long amount, long potSize, TxData txData)
{
    public static readonly TimeSpan PayInterval = TimeSpan.FromSeconds(2);

    private readonly ConcurrentHashSet<ulong> _claimed = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<ulong> _queue = new();
    private readonly Lock _potLock = new();
    private long _pot = potSize;
    private volatile bool _potEmptied;

    public bool IsPotLimited { get; } = potSize > 0;
    public bool PotEmptied => _potEmptied;

    public long Pot
    {
        get
        {
            lock (_potLock)
                return _pot;
        }
    }

    public bool TryClaim(ulong userId)
    {
        if (!_claimed.Add(userId))
            return false;

        if (IsPotLimited)
        {
            lock (_potLock)
            {
                if (_pot < amount)
                    return false;

                _pot -= amount;
                if (_pot < amount)
                    _potEmptied = true;
            }
        }

        _queue.Enqueue(userId);
        return true;
    }

    public async Task<int> PayQueuedAsync()
    {
        var toPay = new List<ulong>();
        while (_queue.TryDequeue(out var userId))
            toPay.Add(userId);

        if (toPay.Count == 0)
            return 0;

        try
        {
            await cs.AddBulkAsync(toPay, amount, txData);
            return toPay.Count;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unable to pay {Count} currency event members", toPay.Count);
            return 0;
        }
    }

    public async Task RunAsync(TimeSpan duration, Func<long, Task> onPaid, Func<Task> stop, CancellationToken ct)
    {
        var endAt = DateTime.UtcNow + duration;
        using var timer = new PeriodicTimer(PayInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var potEmptied = PotEmptied;
                var paid = await PayQueuedAsync();
                if (paid > 0 && IsPotLimited)
                {
                    try
                    {
                        await onPaid(Pot);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Unable to update the currency event message");
                    }
                }

                if (potEmptied || DateTime.UtcNow >= endAt)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }

        await stop();

        // members who claimed during the last interval were already taken from the pot
        await PayQueuedAsync();
    }
}
