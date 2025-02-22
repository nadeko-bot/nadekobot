using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility;

public sealed class CommandPromptResultModel
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, string> Arguments { get; set; } = new();

    [JsonPropertyName("remaining")]
    [JsonConverter(typeof(NumberToStringConverter))]
    public string Remaining { get; set; } = string.Empty;
}