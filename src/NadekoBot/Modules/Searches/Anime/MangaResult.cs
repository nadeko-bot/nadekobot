using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public sealed class MangaResult
{
    public int Id { get; init; }
    public string TitleEnglish { get; init; } = string.Empty;
    public string PublishingStatus { get; init; } = string.Empty;
    public int TotalChapters { get; init; }
    public int TotalVolumes { get; init; }
    public string? Description { get; init; }
    public string? ImageUrlLge { get; init; }
    public string[] Genres { get; init; } = [];
    public float AverageScore { get; init; }
    public string Link { get; init; } = string.Empty;

    [JsonIgnore]
    public string Synopsis
        => Description?.TrimTo(AnimeResult.SYNOPSIS_LENGTH) ?? string.Empty;
}
