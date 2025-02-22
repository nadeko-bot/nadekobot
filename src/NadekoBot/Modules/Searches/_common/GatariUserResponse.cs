#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class GatariUserData
{
    [JsonProperty("abbr")]
    public object Abbr { get; set; }

    [JsonProperty("clanid")]
    public object Clanid { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("favourite_mode")]
    public int FavouriteMode { get; set; }

    [JsonProperty("followers_count")]
    public int FollowersCount { get; set; }

    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("latest_activity")]
    public int LatestActivity { get; set; }

    [JsonProperty("play_style")]
    public int PlayStyle { get; set; }

    [JsonProperty("privileges")]
    public int Privileges { get; set; }

    [JsonProperty("registered_on")]
    public int RegisteredOn { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; }

    [JsonProperty("username_aka")]
    public string UsernameAka { get; set; }
}

public class GatariUserResponse
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("users")]
    public List<GatariUserData> Users { get; set; }
}