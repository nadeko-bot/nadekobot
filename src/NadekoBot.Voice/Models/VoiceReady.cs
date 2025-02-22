using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class VoiceReady
    {
        [JsonProperty("ssrc")]
        public uint Ssrc { get; set; }

        [JsonProperty("ip")]
        public string Ip { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("modes")]
        public string[] Modes { get; set; }

        [JsonProperty("heartbeat_interval")]
        public string HeartbeatInterval { get; set; }
    }
}