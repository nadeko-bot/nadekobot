using System.Threading.Channels;
using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 4: QueueItem class vs readonly record struct.
/// Measures the cost of creating and consuming queue items through a Channel.
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class QueueItemBenchmarks
{
    private const int ItemCount = 100;

    private sealed class QueueItemClass
    {
        public object Payload { get; }
        public TaskCompletionSource<bool> Result { get; }

        public QueueItemClass(object payload, TaskCompletionSource<bool> result)
        {
            Payload = payload;
            Result = result;
        }
    }

    private readonly record struct QueueItemStruct(object Payload, TaskCompletionSource<bool> Result);

    [Benchmark(Description = "Current: class QueueItem")]
    public int Current_ClassQueueItem()
    {
        var channel = Channel.CreateUnbounded<QueueItemClass>(new() { SingleReader = true });
        var total = 0;

        for (var i = 0; i < ItemCount; i++)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new QueueItemClass("payload", tcs);
            channel.Writer.TryWrite(item);
        }

        while (channel.Reader.TryRead(out var item))
        {
            item.Result.TrySetResult(true);
            total++;
        }

        return total;
    }

    [Benchmark(Description = "Fixed: struct QueueItem")]
    public int Fixed_StructQueueItem()
    {
        var channel = Channel.CreateUnbounded<QueueItemStruct>(new() { SingleReader = true });
        var total = 0;

        for (var i = 0; i < ItemCount; i++)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new QueueItemStruct("payload", tcs);
            channel.Writer.TryWrite(item);
        }

        while (channel.Reader.TryRead(out var item))
        {
            item.Result.TrySetResult(true);
            total++;
        }

        return total;
    }
}
