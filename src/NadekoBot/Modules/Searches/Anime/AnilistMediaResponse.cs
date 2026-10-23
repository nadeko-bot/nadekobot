using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public sealed class AnilistMediaResponse
{
    [JsonPropertyName("data")]
    public AnilistMediaData? Data { get; set; }
}

public sealed class AnilistMediaData
{
    [JsonPropertyName("Media")]
    public AnilistMedia? Media { get; set; }
}

public sealed class AnilistMedia
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    [JsonPropertyName("chapters")]
    public int? Chapters { get; set; }

    [JsonPropertyName("volumes")]
    public int? Volumes { get; set; }

    [JsonPropertyName("averageScore")]
    public int? AverageScore { get; set; }

    [JsonPropertyName("genres")]
    public string[]? Genres { get; set; }

    [JsonPropertyName("title")]
    public AnilistMediaTitle? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("coverImage")]
    public AnilistCoverImage? CoverImage { get; set; }
}

public sealed class AnilistCoverImage
{
    [JsonPropertyName("extraLarge")]
    public string? ExtraLarge { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }
}
