#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches;

public class SteamGameId
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("appid")]
    public int AppId { get; set; }
}

public class SteamGameData
{
    public string ShortDescription { get; set; }

    public class Container
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public SteamGameData Data { get; set; }
    }
}

public enum TimeErrors
{
    InvalidInput,
    ApiKeyMissing,
    NotFound,
    Unknown
}