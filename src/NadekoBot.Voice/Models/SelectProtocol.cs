using Newtonsoft.Json;

namespace NadekoBot.Voice.Models
{
    public sealed class SelectProtocol
    {
        [JsonProperty("protocol")]
        public string Protocol { get; set; } = null!;

        [JsonProperty("data")]
        public ProtocolData Data { get; set; } = null!;

        public sealed class ProtocolData
        {
            [JsonProperty("address")]
            public string Address { get; set; } = null!;
            [JsonProperty("port")]
            public int Port { get; set; }
            [JsonProperty("mode")]
            public string Mode { get; set; } = null!;
        }
    }
}