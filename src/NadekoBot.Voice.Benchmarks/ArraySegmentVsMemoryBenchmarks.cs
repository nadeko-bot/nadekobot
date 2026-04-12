using System.Net.WebSockets;
using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 9: ArraySegment vs ReadOnlyMemory for WebSocket send.
/// Measures construction cost only (actual WS send not benchmarkable).
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class ArraySegmentVsMemoryBenchmarks
{
    private byte[] _data = null!;
    private const int ChunkSize = 4096;

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[8192];
        Random.Shared.NextBytes(_data);
    }

    [Benchmark(Description = "Current: ArraySegment chunking")]
    public int Current_ArraySegment()
    {
        var total = 0;
        for (var i = 0; i < _data.Length; i += ChunkSize)
        {
            var count = i + ChunkSize > _data.Length ? _data.Length - i : ChunkSize;
            var segment = new ArraySegment<byte>(_data, i, count);
            total += segment.Count;
        }
        return total;
    }

    [Benchmark(Description = "Fixed: ReadOnlyMemory chunking")]
    public int Fixed_ReadOnlyMemory()
    {
        var total = 0;
        for (var i = 0; i < _data.Length; i += ChunkSize)
        {
            var count = i + ChunkSize > _data.Length ? _data.Length - i : ChunkSize;
            var memory = _data.AsMemory(i, count);
            total += memory.Length;
        }
        return total;
    }
}
