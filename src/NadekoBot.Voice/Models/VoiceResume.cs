using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class VoiceResume
    {
        [JsonProperty("server_id")]
        public string ServerId { get; set; } = null!;

        [JsonProperty("session_id")]
        public string SessionId { get; set; } = null!;

        [JsonProperty("token")]
        public string Token { get; set; } = null!;

        [JsonProperty("seq_ack")]
        public int SeqAck { get; set; } = -1;
    }
}