#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class TimeZoneResult
{
    [JsonProperty("abbreviation")]
    public string TimezoneName { get; set; }

    [JsonProperty("timestamp")]
    public int Timestamp { get; set; }
}

public class LocationIqResponse
{
    public float Lat { get; set; }
    public float Lon { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; }
}