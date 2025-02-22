#nullable disable
namespace NadekoBot.Db;

public class WaifuInfoStats
{
    public int WaifuId { get; init; }
    public string FullName { get; init; }
    public long Price { get; init; }
    public string ClaimerName { get; init; }
    public string AffinityName { get; init; }
    public int AffinityCount { get; init; }
    public int DivorceCount { get; init; }
    public int ClaimCount { get; init; }
}