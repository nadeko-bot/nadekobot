using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class NasdaqSummaryResponse
{
    public required string Symbol { get; init; }

    public required NasdaqSummaryResponseData SummaryData { get; init; }

    public sealed class NasdaqSummaryResponseData
    {
        public required PreviousCloseData PreviousClose { get; init; }
        public required MarketCapData MarketCap { get; init; }
        public required AverageVolumeData AverageVolume { get; init; }

        public sealed class PreviousCloseData
        {
            public required string Value { get; init; }
        }

        public sealed class MarketCapData
        {
            public required string Value { get; init; }
        }

        public sealed class AverageVolumeData
        {
            public required string Value { get; init; }
        }
    }
}