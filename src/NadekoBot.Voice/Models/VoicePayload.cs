using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Discord.Models.Gateway
{
    public sealed class VoicePayload
    {
        [JsonProperty("op")]
        public VoiceOpCode OpCode { get; set; }

        [JsonProperty("d")]
        public JToken Data { get; set; }
    }
    
    public enum VoiceOpCode
    {
        Identify = 0,
        SelectProtocol = 1,
        Ready = 2,
        Heartbeat = 3,
        SessionDescription = 4,
        Speaking = 5,
        HeartbeatAck = 6,
        Resume = 7,
        Hello = 8,
        Resumed = 9,
        ClientDisconnect = 13,
    }
}