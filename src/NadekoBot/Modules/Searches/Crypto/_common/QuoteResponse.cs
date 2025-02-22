#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public class QuoteResponse
{
    public class ResultModel
    {
        [JsonPropertyName("longName")]
        public string LongName { get; set; }

        [JsonPropertyName("regularMarketPrice")]
        public double RegularMarketPrice { get; set; }

        [JsonPropertyName("regularMarketPreviousClose")]
        public double RegularMarketPreviousClose { get; set; }

        [JsonPropertyName("fullExchangeName")]
        public string FullExchangeName { get; set; }

        [JsonPropertyName("averageDailyVolume10Day")]
        public int AverageDailyVolume10Day { get; set; }

        [JsonPropertyName("fiftyDayAverageChangePercent")]
        public double FiftyDayAverageChangePercent { get; set; }

        [JsonPropertyName("twoHundredDayAverageChangePercent")]
        public double TwoHundredDayAverageChangePercent { get; set; }

        [JsonPropertyName("marketCap")]
        public long MarketCap { get; set; }

        [JsonPropertyName("symbol")]
        public string Symbol { get; set; }
    }
    
    [JsonPropertyName("result")]
    public List<ResultModel> Result { get; set; }

    [JsonPropertyName("error")]
    public object Error { get; set; }
}