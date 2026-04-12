using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class VoiceSessionDescription
    {
        [JsonProperty("mode")]
        public string Mode { get; set; } = null!;

        [JsonProperty("secret_key")]
        public byte[] SecretKey { get; set; } = null!;

        [JsonProperty("dave_protocol_version")]
        public int DaveProtocolVersion { get; set; }
    }
}