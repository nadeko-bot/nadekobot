#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Common;

public class OsuUserBests
{
    [JsonProperty("beatmap_id")]
    public string BeatmapId { get; set; }

    [JsonProperty("score_id")]
    public string ScoreId { get; set; }

    [JsonProperty("score")]
    public string Score { get; set; }

    [JsonProperty("maxcombo")]
    public string Maxcombo { get; set; }

    [JsonProperty("count50")]
    public double Count50 { get; set; }

    [JsonProperty("count100")]
    public double Count100 { get; set; }

    [JsonProperty("count300")]
    public double Count300 { get; set; }

    [JsonProperty("countmiss")]
    public int Countmiss { get; set; }

    [JsonProperty("countkatu")]
    public double Countkatu { get; set; }

    [JsonProperty("countgeki")]
    public double Countgeki { get; set; }

    [JsonProperty("perfect")]
    public string Perfect { get; set; }

    [JsonProperty("enabled_mods")]
    public int EnabledMods { get; set; }

    [JsonProperty("user_id")]
    public string UserId { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }

    [JsonProperty("rank")]
    public string Rank { get; set; }

    [JsonProperty("pp")]
    public double Pp { get; set; }

    [JsonProperty("replay_available")]
    public string ReplayAvailable { get; set; }
}