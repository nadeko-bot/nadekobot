using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility;

public sealed class AiCommandModel
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("desc")]
    public required string? Desc { get; set; }

    [JsonPropertyName("params")]
    public required IReadOnlyList<AiCommandParamModel> Params { get; set; }
}