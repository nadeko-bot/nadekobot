using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 5: GetRecognizedUserIds - List+ToArray vs pre-sized array.
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class GetRecognizedUserIdsBenchmarks
{
    private HashSet<string> _recognizedUserIds = null!;
    private string _selfUserId = null!;

    [Params(5, 20, 50)]
    public int UserCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _selfUserId = "123456789";
        _recognizedUserIds = new HashSet<string>();
        for (var i = 0; i < UserCount; i++)
            _recognizedUserIds.Add((100000000 + i).ToString());
        // self is NOT in the set, so we test the add path
    }

    [Benchmark(Description = "Current: List+Add+ToArray")]
    public string[] Current_ListToArray()
    {
        var list = new List<string>(_recognizedUserIds);
        if (!list.Contains(_selfUserId))
            list.Add(_selfUserId);
        return list.ToArray();
    }

    [Benchmark(Description = "Fixed: Pre-sized array")]
    public string[] Fixed_PreSizedArray()
    {
        var hasSelf = _recognizedUserIds.Contains(_selfUserId);
        var count = _recognizedUserIds.Count + (hasSelf ? 0 : 1);
        var result = new string[count];
        _recognizedUserIds.CopyTo(result);
        if (!hasSelf)
            result[^1] = _selfUserId;
        return result;
    }
}
