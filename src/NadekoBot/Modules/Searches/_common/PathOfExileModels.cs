#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class Account
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("league")]
    public string League { get; set; }

    [JsonProperty("classId")]
    public int ClassId { get; set; }

    [JsonProperty("ascendancyClass")]
    public int AscendancyClass { get; set; }

    [JsonProperty("class")]
    public string Class { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }
}

public class Leagues
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("startAt")]
    public DateTime StartAt { get; set; }

    [JsonProperty("endAt")]
    public object EndAt { get; set; }
}