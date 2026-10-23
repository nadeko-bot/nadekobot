using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public sealed class AnimeResult
{
    public const int SYNOPSIS_LENGTH = 500;

    public int Id { get; init; }
    public string TitleEnglish { get; init; } = string.Empty;
    public string AiringStatus { get; init; } = string.Empty;
    public int TotalEpisodes { get; init; }
    public string? Description { get; init; }
    public string? ImageUrlLarge { get; init; }
    public string[] Genres { get; init; } = [];
    public float AverageScore { get; init; }
    public string Link { get; init; } = string.Empty;

    [JsonIgnore]
    public string Synopsis
        => Description?.TrimTo(SYNOPSIS_LENGTH) ?? string.Empty;
}
