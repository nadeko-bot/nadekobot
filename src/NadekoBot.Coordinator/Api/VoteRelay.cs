#nullable enable
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NadekoBot.Coordinator.Api;

// NadekoBot.VotesApi puts each vote here, and shard 0 takes them; the queue keeps them while shard 0 restarts.
public sealed class VoteRelay
{
    public const int SHARD_ID = 0;
    public const int CAPACITY = 1000;
    public const int MAX_BATCH = 50;

    public const string TOPGG = "topgg";
    public const string DISCORDBOTLIST = "discordbotlist";
    public const string DISCORDS = "discords";

    // Below the shards' 10 s request timeout.
    public static readonly TimeSpan LongPoll = TimeSpan.FromSeconds(5);

    private readonly Channel<QueuedVote> _votes = Channel.CreateBounded<QueuedVote>(new BoundedChannelOptions(CAPACITY)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public static bool TryGetType(string? text, out string type)
    {
        switch (text)
        {
            case TOPGG:
                type = TOPGG;
                return true;
            case DISCORDBOTLIST:
                type = DISCORDBOTLIST;
                return true;
            case DISCORDS:
                type = DISCORDS;
                return true;
            default:
                type = string.Empty;
                return false;
        }
    }

    // False when the queue is full.
    public bool TryAdd(ulong userId, string type)
        => _votes.Writer.TryWrite(new(userId, type));

    // Returns when a vote waits, after LongPoll, or when the caller leaves.
    public async Task WaitAsync(CancellationToken aborted)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        wait.CancelAfter(LongPoll);
        try
        {
            await _votes.Reader.WaitToReadAsync(wait.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public VoteDto[] Take()
    {
        var batch = new VoteDto[Math.Min(_votes.Reader.Count, MAX_BATCH)];
        var count = 0;
        while (count < batch.Length && _votes.Reader.TryRead(out var vote))
            batch[count++] = new(vote.UserId.ToString(CultureInfo.InvariantCulture), vote.Type);

        return count == batch.Length ? batch : batch[..count];
    }

    private readonly record struct QueuedVote(ulong UserId, string Type);
}
