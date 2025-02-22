#nullable disable
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace NadekoBot;

public class SmartTextEmbedAuthor
{
    public string Name { get; set; }

    [JsonProperty("icon_url")]
    [JsonPropertyName("icon_url")]
    public string IconUrl { get; set; }

    public string Url { get; set; }
}