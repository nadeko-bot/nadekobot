namespace NadekoBot.Modules.Searches;

public sealed class NasdaqChartResponse
{
    public required NasdaqChartResponseData[] Chart { get; init; }

    public sealed class NasdaqChartResponseData
    {
        public required CandleData Z { get; init; }

        public sealed class CandleData
        {
            public required decimal High { get; init; }
            public required decimal Low { get; init; }
            public required decimal Open { get; init; }
            public required decimal Close { get; init; }
            public required string Volume { get; init; }
        }
    }
}