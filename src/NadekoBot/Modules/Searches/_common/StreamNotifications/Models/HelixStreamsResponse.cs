#nullable disable
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Searches.Common.StreamNotifications.Providers;

public class HelixStreamsResponse
{
    public class PaginationData
    {
        [JsonPropertyName("cursor")]
        public string Cursor { get; set; }
    }
    
    public class StreamData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; }

        [JsonPropertyName("user_name")]
        public string UserName { get; set; }

        [JsonPropertyName("game_id")]
        public string GameId { get; set; }

        [JsonPropertyName("game_name")]
        public string GameName { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("viewer_count")]
        public int ViewerCount { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("thumbnail_url")]
        public string ThumbnailUrl { get; set; }

        [JsonPropertyName("tag_ids")]
        public List<string> TagIds { get; set; }

        [JsonPropertyName("is_mature")]
        public bool IsMature { get; set; }
    }
    
    [JsonPropertyName("data")]
    public List<StreamData> Data { get; set; }

    [JsonPropertyName("pagination")]
    public PaginationData Pagination { get; set; }
}