#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class TimeZoneResult
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("zoneName")]
    public string ZoneName { get; set; }
}

public class LocationIqResponse
{
    public float Lat { get; set; }
    public float Lon { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; }
}