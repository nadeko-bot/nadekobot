using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common;

public sealed class AnilistUserResponse
{
    [JsonPropertyName("data")]
    public AnilistUserData? Data { get; set; }
}

public sealed class AnilistUserData
{
    [JsonPropertyName("User")]
    public AnilistUser? User { get; set; }
}

public sealed class AnilistUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("siteUrl")]
    public string SiteUrl { get; set; } = null!;

    [JsonPropertyName("avatar")]
    public AnilistUserAvatar? Avatar { get; set; }

    [JsonPropertyName("bannerImage")]
    public string? BannerImage { get; set; }

    [JsonPropertyName("about")]
    public string? About { get; set; }

    [JsonPropertyName("statistics")]
    public AnilistUserStatistics? Statistics { get; set; }

    [JsonPropertyName("favourites")]
    public AnilistUserFavourites? Favourites { get; set; }
}

public sealed class AnilistUserAvatar
{
    [JsonPropertyName("large")]
    public string? Large { get; set; }
}

public sealed class AnilistUserStatistics
{
    [JsonPropertyName("anime")]
    public AnilistAnimeStats? Anime { get; set; }

    [JsonPropertyName("manga")]
    public AnilistMangaStats? Manga { get; set; }
}

public sealed class AnilistAnimeStats
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("meanScore")]
    public float MeanScore { get; set; }

    [JsonPropertyName("minutesWatched")]
    public int MinutesWatched { get; set; }

    [JsonPropertyName("episodesWatched")]
    public int EpisodesWatched { get; set; }
}

public sealed class AnilistMangaStats
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("meanScore")]
    public float MeanScore { get; set; }

    [JsonPropertyName("chaptersRead")]
    public int ChaptersRead { get; set; }

    [JsonPropertyName("volumesRead")]
    public int VolumesRead { get; set; }
}

public sealed class AnilistUserFavourites
{
    [JsonPropertyName("anime")]
    public AnilistFavouriteConnection? Anime { get; set; }

    [JsonPropertyName("manga")]
    public AnilistFavouriteConnection? Manga { get; set; }

    [JsonPropertyName("characters")]
    public AnilistFavouriteConnection? Characters { get; set; }
}

public sealed class AnilistFavouriteConnection
{
    [JsonPropertyName("pageInfo")]
    public AnilistPageInfo? PageInfo { get; set; }
}

public sealed class AnilistPageInfo
{
    [JsonPropertyName("total")]
    public int Total { get; set; }
}
