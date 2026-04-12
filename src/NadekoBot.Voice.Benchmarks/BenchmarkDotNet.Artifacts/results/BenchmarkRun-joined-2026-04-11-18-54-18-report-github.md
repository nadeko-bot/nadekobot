```

BenchmarkDotNet v0.15.8, Linux Artix Linux
AMD Ryzen 7 PRO 6850U with Radeon Graphics 1.93GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 9.0.312
  [Host]   : .NET 9.0.14 (9.0.14, 9.0.1426.11910), X64 RyuJIT x86-64-v3
  ShortRun : .NET 9.0.14 (9.0.14, 9.0.1426.11910), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=10  LaunchCount=1  
WarmupCount=5  

```
| Type                           | Method                                 | InvocationCount | UnrollFactor | UserCount | Mean         | Error         | StdDev      | Gen0   | Gen1   | Allocated |
|------------------------------- |--------------------------------------- |---------------- |------------- |---------- |-------------:|--------------:|------------:|-------:|-------:|----------:|
| **ArraySegmentVsMemoryBenchmarks** | **&#39;Current: ArraySegment chunking&#39;**       | **Default**         | **16**           | **?**         |     **1.480 ns** |     **0.0567 ns** |   **0.0338 ns** |      **-** |      **-** |         **-** |
| CloseCodesBenchmarks           | &#39;Current: ReadOnlyDictionary&#39;          | Default         | 16           | ?         |    38.195 ns |     0.9843 ns |   0.5858 ns |      - |      - |         - |
| DaveAudioPayloadBenchmarks     | &#39;Current: Rent+Copy+Return&#39;            | Default         | 16           | ?         |   134.004 ns |    12.3198 ns |   8.1488 ns | 0.1205 |      - |    1008 B |
| IpDiscoveryBenchmarks          | &#39;Current: new byte[74] + BitConverter&#39; | Default         | 16           | ?         |    26.882 ns |     3.2106 ns |   2.1236 ns | 0.0162 |      - |     136 B |
| QueueItemBenchmarks            | &#39;Current: class QueueItem&#39;             | Default         | 16           | ?         | 6,369.996 ns |   669.6255 ns | 442.9158 ns | 1.9608 | 0.0610 |   16408 B |
| ArraySegmentVsMemoryBenchmarks | &#39;Fixed: ReadOnlyMemory chunking&#39;       | Default         | 16           | ?         |     1.495 ns |     0.0236 ns |   0.0140 ns |      - |      - |         - |
| CloseCodesBenchmarks           | &#39;Fixed: FrozenDictionary&#39;              | Default         | 16           | ?         |    14.444 ns |     0.3489 ns |   0.2308 ns |      - |      - |         - |
| DaveAudioPayloadBenchmarks     | &#39;Fixed: Rent+Use+Return&#39;               | Default         | 16           | ?         |    90.534 ns |     1.4235 ns |   0.8471 ns |      - |      - |         - |
| IpDiscoveryBenchmarks          | &#39;Fixed: stackalloc + BinaryPrimitives&#39; | Default         | 16           | ?         |     1.254 ns |     0.0410 ns |   0.0244 ns |      - |      - |         - |
| QueueItemBenchmarks            | &#39;Fixed: struct QueueItem&#39;              | Default         | 16           | ?         | 5,441.117 ns |   478.4857 ns | 284.7392 ns | 1.7929 | 0.0687 |   15008 B |
| PoopyBufferReadBenchmarks      | &#39;Current: Copy to output array&#39;        | 1               | 1            | ?         | 4,321.375 ns |   322.7510 ns | 168.8050 ns |      - |      - |         - |
| PoopyBufferReadBenchmarks      | &#39;Fixed: Direct span, no copy&#39;          | 1               | 1            | ?         | 2,081.800 ns | 1,011.3266 ns | 668.9300 ns |      - |      - |         - |
| **GetRecognizedUserIdsBenchmarks** | **&#39;Current: List+Add+ToArray&#39;**            | **Default**         | **16**           | **5**         |   **113.693 ns** |    **12.0813 ns** |   **7.9910 ns** | **0.0324** |      **-** |     **272 B** |
| GetRecognizedUserIdsBenchmarks | &#39;Fixed: Pre-sized array&#39;               | Default         | 16           | 5         |    39.525 ns |     5.1983 ns |   3.4384 ns | 0.0086 |      - |      72 B |
| **GetRecognizedUserIdsBenchmarks** | **&#39;Current: List+Add+ToArray&#39;**            | **Default**         | **16**           | **20**        |   **244.769 ns** |    **21.7416 ns** |  **14.3807 ns** | **0.0899** |      **-** |     **752 B** |
| GetRecognizedUserIdsBenchmarks | &#39;Fixed: Pre-sized array&#39;               | Default         | 16           | 20        |    92.802 ns |     7.6136 ns |   5.0359 ns | 0.0229 |      - |     192 B |
| **GetRecognizedUserIdsBenchmarks** | **&#39;Current: List+Add+ToArray&#39;**            | **Default**         | **16**           | **50**        |   **484.641 ns** |    **32.7018 ns** |  **19.4603 ns** | **0.2041** |      **-** |    **1712 B** |
| GetRecognizedUserIdsBenchmarks | &#39;Fixed: Pre-sized array&#39;               | Default         | 16           | 50        |   197.586 ns |     6.9827 ns |   4.6186 ns | 0.0515 |      - |     432 B |
