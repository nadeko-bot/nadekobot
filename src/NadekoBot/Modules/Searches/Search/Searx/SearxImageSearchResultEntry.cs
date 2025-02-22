using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches;

public sealed class SearxImageSearchResultEntry : IImageSearchResultEntry
{
    public string Link
        => ImageSource.StartsWith("//")
            ? "https:" + ImageSource
            : ImageSource;

    [JsonPropertyName("img_src")]
    public string ImageSource { get; set; } = string.Empty;
}