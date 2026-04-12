using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class VoiceIdentify
    {
        [JsonProperty("server_id")]
        public string ServerId { get; set; } = null!;

        [JsonProperty("user_id")]
        public string UserId { get; set; } = null!;

        [JsonProperty("session_id")]
        public string SessionId { get; set; } = null!;

        [JsonProperty("token")]
        public string Token { get; set; } = null!;

        [JsonProperty("max_dave_protocol_version")]
        public int MaxDaveProtocolVersion { get; set; } = 1;
    }
}