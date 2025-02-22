#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class TwitchUsersResponseV5
{
    [JsonProperty("users")]
    public List<User> Users { get; set; }

    public class User
    {
        [JsonProperty("_id")]
        public string Id { get; set; }

        // [JsonProperty("bio")]
        // public string Bio { get; set; } 
        //
        // [JsonProperty("created_at")]
        // public DateTime CreatedAt { get; set; } 
        //
        // [JsonProperty("display_name")]
        // public string DisplayName { get; set; } 
        //
        // [JsonProperty("logo")]
        // public string Logo { get; set; } 
        //
        // [JsonProperty("name")]
        // public string Name { get; set; } 
        //
        // [JsonProperty("type")]
        // public string Type { get; set; } 
        //
        // [JsonProperty("updated_at")]
        // public DateTime UpdatedAt { get; set; } 
    }
}