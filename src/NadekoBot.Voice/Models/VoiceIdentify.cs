using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class VoiceIdentify
    {
        [JsonProperty("server_id")]
        public string ServerId { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("session_id")]
        public string SessionId { get; set; }

        [JsonProperty("token")]
        public string Token { get; set; }

    }
}