using Newtonsoft.Json;
using System;

namespace NadekoBot.Voice.Models
{
    public sealed class VoiceSpeaking
    {
        [JsonProperty("speaking")]
        public int Speaking { get; set; }

        [JsonProperty("delay")]
        public int Delay { get; set; }

        [JsonProperty("ssrc")]
        public uint Ssrc { get; set; }

        [Flags]
        public enum State
        {
            None = 0,
            Microphone = 1 << 0,
            Soundshare = 1 << 1,
            Priority = 1 << 2
        }
    }
}