using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class SelectProtocol
    {
        [JsonProperty("protocol")]
        public string Protocol { get; set; }

        [JsonProperty("data")]
        public ProtocolData Data { get; set; }

        public sealed class ProtocolData
        {
            [JsonProperty("address")]
            public string Address { get; set; }
            [JsonProperty("port")]
            public int Port { get; set; }
            [JsonProperty("mode")]
            public string Mode { get; set; }
        }
    }
}