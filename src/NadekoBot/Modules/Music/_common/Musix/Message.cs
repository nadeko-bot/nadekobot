using System.Text.Json.Serialization;

namespace Musix.Models;

public class Message<T>
{
    [JsonPropertyName("header")]
    public Header Header { get; set; } = null!;

    [JsonPropertyName("body")]
    public T Body { get; set; } = default!;
}