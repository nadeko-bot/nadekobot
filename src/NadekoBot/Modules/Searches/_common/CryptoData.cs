#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public class CryptoResponse
{
    public List<CmcResponseData> Data { get; set; }
}

public class CmcQuote
{
    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("volume_24h")]
    public double Volume24h { get; set; }

    // [JsonPropertyName("volume_change_24h")]
    // public double VolumeChange24h { get; set; }
    //
    // [JsonPropertyName("percent_change_1h")]
    // public double PercentChange1h { get; set; }

    [JsonPropertyName("percent_change_24h")]
    public double PercentChange24h { get; set; }

    [JsonPropertyName("percent_change_7d")]
    public double PercentChange7d { get; set; }

    [JsonPropertyName("market_cap")]
    public double MarketCap { get; set; }

    [JsonPropertyName("market_cap_dominance")]
    public double MarketCapDominance { get; set; }
}

public class CmcResponseData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; }

    [JsonPropertyName("slug")]
    public string Slug { get; set; }

    [JsonPropertyName("cmc_rank")]
    public int CmcRank { get; set; }

    [JsonPropertyName("circulating_supply")]
    public double? CirculatingSupply { get; set; }

    [JsonPropertyName("total_supply")]
    public double? TotalSupply { get; set; }

    [JsonPropertyName("max_supply")]
    public double? MaxSupply { get; set; }

    [JsonPropertyName("quote")]
    public Dictionary<string, CmcQuote> Quote { get; set; }
}