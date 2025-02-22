#nullable disable
using Newtonsoft.Json;

namespace NadekoBot.Modules.Searches.Common;

public class TwitchResponseV5
{
    public List<Stream> Streams { get; set; }

    public class Channel
    {
        [JsonProperty("_id")]
        public int Id { get; set; }

        [JsonProperty("broadcaster_language")]
        public string BroadcasterLanguage { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("followers")]
        public int Followers { get; set; }

        [JsonProperty("game")]
        public string Game { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("logo")]
        public string Logo { get; set; }

        [JsonProperty("mature")]
        public bool Mature { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("partner")]
        public bool Partner { get; set; }

        [JsonProperty("profile_banner")]
        public string ProfileBanner { get; set; }

        [JsonProperty("profile_banner_background_color")]
        public object ProfileBannerBackgroundColor { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("video_banner")]
        public string VideoBanner { get; set; }

        [JsonProperty("views")]
        public int Views { get; set; }
    }

    public class Preview
    {
        [JsonProperty("large")]
        public string Large { get; set; }

        [JsonProperty("medium")]
        public string Medium { get; set; }

        [JsonProperty("small")]
        public string Small { get; set; }

        [JsonProperty("template")]
        public string Template { get; set; }
    }

    public class Stream
    {
        [JsonProperty("_id")]
        public long Id { get; set; }

        [JsonProperty("average_fps")]
        public double AverageFps { get; set; }

        [JsonProperty("channel")]
        public Channel Channel { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("delay")]
        public double Delay { get; set; }

        [JsonProperty("game")]
        public string Game { get; set; }

        [JsonProperty("is_playlist")]
        public bool IsPlaylist { get; set; }

        [JsonProperty("preview")]
        public Preview Preview { get; set; }

        [JsonProperty("video_height")]
        public int VideoHeight { get; set; }

        [JsonProperty("viewers")]
        public int Viewers { get; set; }
    }
}