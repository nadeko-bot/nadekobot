using System.Buffers;
using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 8: stackalloc IP discovery packet vs heap allocation.
/// Also covers BitConverter.GetBytes+Reverse vs BinaryPrimitives.
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class IpDiscoveryBenchmarks
{
    private uint _ssrc;

    [GlobalSetup]
    public void Setup()
    {
        _ssrc = 12345u;
    }

    [Benchmark(Description = "Current: new byte[74] + BitConverter")]
    public int Current_HeapAlloc()
    {
        var ssrcBytes = BitConverter.GetBytes(_ssrc);
        Array.Reverse(ssrcBytes);
        var ipDiscoveryData = new byte[74];
        Buffer.BlockCopy(ssrcBytes, 0, ipDiscoveryData, 4, ssrcBytes.Length);
        ipDiscoveryData[0] = 0x00;
        ipDiscoveryData[1] = 0x01;
        ipDiscoveryData[2] = 0x00;
        ipDiscoveryData[3] = 0x46;
        return ipDiscoveryData[4]; // consume to prevent dead code elimination
    }

    [Benchmark(Description = "Fixed: stackalloc + BinaryPrimitives")]
    public int Fixed_Stackalloc()
    {
        Span<byte> ipDiscoveryData = stackalloc byte[74];
        ipDiscoveryData[0] = 0x00;
        ipDiscoveryData[1] = 0x01;
        ipDiscoveryData[2] = 0x00;
        ipDiscoveryData[3] = 0x46;
        BinaryPrimitives.WriteUInt32BigEndian(ipDiscoveryData.Slice(4), _ssrc);
        return ipDiscoveryData[4];
    }
}
