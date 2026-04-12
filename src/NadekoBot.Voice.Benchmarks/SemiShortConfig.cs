using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace NadekoBot.Voice.Benchmarks;

public sealed class SemiShortConfig : ManualConfig
{
    public SemiShortConfig()
    {
        AddJob(Job.ShortRun
            .WithWarmupCount(5)
            .WithIterationCount(10));
    }
}
