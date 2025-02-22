#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Games.Common.ChatterBot;

public class OpenAiCompletionResponse
{
    [JsonPropertyName("choices")]
    public Choice[] Choices { get; set; }
    
    [JsonPropertyName("usage")]
    public OpenAiApiUsageData Usage { get; set; }
}