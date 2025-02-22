#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

public class TrovoGetUsersResponse
{
    [JsonPropertyName("is_live")]
    public bool IsLive { get; set; }

    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; }

    [JsonPropertyName("live_title")]
    public string LiveTitle { get; set; }

    [JsonPropertyName("audi_type")]
    public string AudiType { get; set; }

    [JsonPropertyName("language_code")]
    public string LanguageCode { get; set; }

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; }

    [JsonPropertyName("current_viewers")]
    public int CurrentViewers { get; set; }

    [JsonPropertyName("followers")]
    public int Followers { get; set; }

    [JsonPropertyName("streamer_info")]
    public string StreamerInfo { get; set; }

    [JsonPropertyName("profile_pic")]
    public string ProfilePic { get; set; }

    [JsonPropertyName("channel_url")]
    public string ChannelUrl { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; }

    [JsonPropertyName("subscriber_num")]
    public int SubscriberNum { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; }

    [JsonPropertyName("social_links")]
    public List<TrovoSocialLink> SocialLinks { get; set; }

    [JsonPropertyName("started_at")]
    public string StartedAt { get; set; }

    [JsonPropertyName("ended_at")]
    public string EndedAt { get; set; }
}