```

BenchmarkDotNet v0.15.8, Linux Artix Linux
AMD Ryzen 7 PRO 6850U with Radeon Graphics 1.09GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 9.0.312
  [Host]   : .NET 9.0.14 (9.0.14, 9.0.1426.11910), X64 RyuJIT x86-64-v3
  ShortRun : .NET 9.0.14 (9.0.14, 9.0.1426.11910), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=10  LaunchCount=1  
WarmupCount=5  

```
| Method                      | Mean      | Error     | StdDev    | Gen0   | Allocated |
|---------------------------- |----------:|----------:|----------:|-------:|----------:|
| &#39;Current: Rent+Copy+Return&#39; | 138.71 ns | 19.614 ns | 12.973 ns | 0.1204 |    1008 B |
| &#39;Fixed: Rent+Use+Return&#39;    |  72.15 ns |  2.378 ns |  1.244 ns |      - |         - |
