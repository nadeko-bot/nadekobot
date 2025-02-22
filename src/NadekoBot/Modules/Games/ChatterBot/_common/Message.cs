using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Games.Common.ChatterBot;

public class Message
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}