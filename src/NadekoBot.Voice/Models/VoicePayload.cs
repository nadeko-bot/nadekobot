using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Discord.Models.Gateway
{
    public sealed class VoicePayload
    {
        [JsonProperty("op")]
        public VoiceOpCode OpCode { get; set; }

        [JsonProperty("d")]
        public JToken Data { get; set; } = null!;
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
        ClientsConnect = 11,
        ClientDisconnect = 13,
        DavePrepareTransition = 21,
        DaveExecuteTransition = 22,
        DaveTransitionReady = 23,
        DavePrepareEpoch = 24,
        DaveMlsExternalSender = 25,
        DaveMlsKeyPackage = 26,
        DaveMlsProposals = 27,
        DaveMlsCommitWelcome = 28,
        DaveMlsAnnounceCommitTransition = 29,
        DaveMlsWelcome = 30,
        DaveMlsInvalidCommitWelcome = 31,
    }
}