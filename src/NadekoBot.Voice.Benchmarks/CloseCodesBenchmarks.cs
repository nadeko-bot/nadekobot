using System.Collections.Frozen;
using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;

namespace NadekoBot.Voice.Benchmarks;

/// <summary>
/// Fix 3: FrozenDictionary for CloseCodes lookup.
/// </summary>
[Config(typeof(SemiShortConfig))]
[MemoryDiagnoser]
public class CloseCodesBenchmarks
{
    private static readonly IReadOnlyDictionary<int, (string, string, bool)> _readOnlyDict =
        new ReadOnlyDictionary<int, (string, string, bool)>(
            new Dictionary<int, (string Error, string Message, bool ShouldReconnect)>
            {
                { 4001, ("Unknown opcode", "You sent an invalid opcode.", true) },
                { 4002, ("Failed to decode payload", "You sent an invalid payload.", true) },
                { 4003, ("Not authenticated", "You sent a payload before identifying.", true) },
                { 4004, ("Authentication failed", "The token is incorrect.", false) },
                { 4005, ("Already authenticated", "You sent more than one identify.", true) },
                { 4006, ("Session no longer valid", "Your session is no longer valid.", true) },
                { 4009, ("Session timeout", "Your session has timed out.", true) },
                { 4011, ("Server not found", "Can't find the server.", false) },
                { 4012, ("Unknown protocol", "Didn't recognize the protocol.", false) },
                { 4014, ("Disconnected", "Disconnected from voice.", false) },
                { 4015, ("Voice server crashed", "Try resuming.", true) },
                { 4016, ("Unknown encryption mode", "Didn't recognize encryption.", false) },
                { 4017, ("E2EE required", "DAVE protocol required.", false) },
                { 4020, ("Bad request", "Malformed request.", false) },
                { 4021, ("Rate Limited", "Rate limit exceeded.", false) },
                { 4022, ("Call Terminated", "Call was terminated.", false) },
            });

    private static readonly FrozenDictionary<int, (string, string, bool)> _frozenDict =
        new Dictionary<int, (string, string, bool)>
        {
            { 4001, ("Unknown opcode", "You sent an invalid opcode.", true) },
            { 4002, ("Failed to decode payload", "You sent an invalid payload.", true) },
            { 4003, ("Not authenticated", "You sent a payload before identifying.", true) },
            { 4004, ("Authentication failed", "The token is incorrect.", false) },
            { 4005, ("Already authenticated", "You sent more than one identify.", true) },
            { 4006, ("Session no longer valid", "Your session is no longer valid.", true) },
            { 4009, ("Session timeout", "Your session has timed out.", true) },
            { 4011, ("Server not found", "Can't find the server.", false) },
            { 4012, ("Unknown protocol", "Didn't recognize the protocol.", false) },
            { 4014, ("Disconnected", "Disconnected from voice.", false) },
            { 4015, ("Voice server crashed", "Try resuming.", true) },
            { 4016, ("Unknown encryption mode", "Didn't recognize encryption.", false) },
            { 4017, ("E2EE required", "DAVE protocol required.", false) },
            { 4020, ("Bad request", "Malformed request.", false) },
            { 4021, ("Rate Limited", "Rate limit exceeded.", false) },
            { 4022, ("Call Terminated", "Call was terminated.", false) },
        }.ToFrozenDictionary();

    private readonly int[] _lookupKeys = [4001, 4004, 4009, 4015, 4022, 9999]; // mix of hit and miss

    [Benchmark(Description = "Current: ReadOnlyDictionary")]
    public int Current_ReadOnlyDict()
    {
        var total = 0;
        foreach (var key in _lookupKeys)
        {
            if (_readOnlyDict.TryGetValue(key, out var data))
                total += data.Item3 ? 1 : 0;
        }
        return total;
    }

    [Benchmark(Description = "Fixed: FrozenDictionary")]
    public int Fixed_FrozenDict()
    {
        var total = 0;
        foreach (var key in _lookupKeys)
        {
            if (_frozenDict.TryGetValue(key, out var data))
                total += data.Item3 ? 1 : 0;
        }
        return total;
    }
}
