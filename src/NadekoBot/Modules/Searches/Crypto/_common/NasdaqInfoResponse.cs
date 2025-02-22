namespace NadekoBot.Modules.Searches;

public sealed class NasdaqInfoResponse
{
    public required string Symbol { get; init; }
    public required string CompanyName {get; init; }
    public required NasdaqInfoPrimaryData PrimaryData { get; init; }
    
    public sealed class NasdaqInfoPrimaryData
    {
        public required string LastSalePrice{ get; init; }
        public required string PercentageChange { get; init; }
        public required string DeltaIndicator { get; init; }
    }
}