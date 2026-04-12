using System.Buffers;
using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 2: PoopyBuffer skip-copy in non-wrapping Read path.
/// Simulates the ring buffer read with and without the intermediate copy.
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class PoopyBufferReadBenchmarks
{
    private byte[] _buffer = null!;
    private byte[] _outputArray = null!;
    private const int BufferSize = 1_000_000;
    private const int FrameSize = 3840; // 20ms of 48kHz stereo 16-bit PCM
    private int _readPosition;
    private int _writePosition;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        _outputArray = new byte[FrameSize];
        Random.Shared.NextBytes(_buffer);
        _readPosition = 0;
        _writePosition = 100_000; // plenty of data ahead
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _readPosition = 0;
        _writePosition = 100_000;
    }

    [Benchmark(Description = "Current: Copy to output array")]
    public int Current_CopyToOutput()
    {
        var total = 0;
        for (var i = 0; i < 26; i++) // ~26 reads per iteration batch
        {
            var toRead = FrameSize;
            // non-wrapping path (common case)
            var bufferSpan = (Span<byte>)_buffer;
            Span<byte> toReturn = _outputArray;
            bufferSpan.Slice(_readPosition, toRead).CopyTo(toReturn);
            _readPosition += toRead;

            // simulate volume adjust (in-place mutation)
            for (var j = 0; j < 16; j++)
                toReturn[j] = (byte)(toReturn[j] >> 1);

            total += toReturn[0];
        }
        return total;
    }

    [Benchmark(Description = "Fixed: Direct span, no copy")]
    public int Fixed_DirectSpan()
    {
        var total = 0;
        for (var i = 0; i < 26; i++)
        {
            var toRead = FrameSize;
            // return span directly over buffer - no copy
            var toReturn = _buffer.AsSpan(_readPosition, toRead);
            _readPosition += toRead;

            // simulate volume adjust (in-place mutation on buffer, which is fine)
            for (var j = 0; j < 16; j++)
                toReturn[j] = (byte)(toReturn[j] >> 1);

            total += toReturn[0];
        }
        return total;
    }
}
