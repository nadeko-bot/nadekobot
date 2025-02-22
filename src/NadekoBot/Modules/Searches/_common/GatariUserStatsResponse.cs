#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class GatariUserStats
{
    [JsonProperty("a_count")]
    public int ACount { get; set; }

    [JsonProperty("avg_accuracy")]
    public double AvgAccuracy { get; set; }

    [JsonProperty("avg_hits_play")]
    public double AvgHitsPlay { get; set; }

    [JsonProperty("country_rank")]
    public int CountryRank { get; set; }

    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("level_progress")]
    public int LevelProgress { get; set; }

    [JsonProperty("max_combo")]
    public int MaxCombo { get; set; }

    [JsonProperty("playcount")]
    public int Playcount { get; set; }

    [JsonProperty("playtime")]
    public int Playtime { get; set; }

    [JsonProperty("pp")]
    public int Pp { get; set; }

    [JsonProperty("rank")]
    public int? Rank { get; set; }

    [JsonProperty("ranked_score")]
    public int RankedScore { get; set; }

    [JsonProperty("replays_watched")]
    public int ReplaysWatched { get; set; }

    [JsonProperty("s_count")]
    public int SCount { get; set; }

    [JsonProperty("sh_count")]
    public int ShCount { get; set; }

    [JsonProperty("total_hits")]
    public int TotalHits { get; set; }

    [JsonProperty("total_score")]
    public long TotalScore { get; set; }

    [JsonProperty("x_count")]
    public int XCount { get; set; }

    [JsonProperty("xh_count")]
    public int XhCount { get; set; }
}

public class GatariUserStatsResponse
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("stats")]
    public GatariUserStats Stats { get; set; }
}