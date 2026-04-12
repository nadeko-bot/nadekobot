using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ayu.Discord.Gateway
{
    public static class CloseCodes
    {
        private static readonly FrozenDictionary<int, (string Error, string Message, bool ShouldReconnect)> _closeCodes =
            new Dictionary<int, (string Error, string Message, bool ShouldReconnect)>
            {
                { 4001, ("Unknown opcode", "You sent an invalid opcode.", true) },
                { 4002, ("Failed to decode payload", "You sent an invalid payload in your identifying to the Gateway.", true) },
                { 4003, ("Not authenticated", "You sent a payload before identifying with the Gateway.", true) },
                { 4004, ("Authentication failed", "The token you sent in your identify payload is incorrect.", false) },
                { 4005, ("Already authenticated", "You sent more than one identify payload.", true) },
                { 4006, ("Session no longer valid", "Your session is no longer valid.", true) },
                { 4009, ("Session timeout", "Your session has timed out.", true) },
                { 4011, ("Server not found", "We can't find the server you're trying to connect to.", false) },
                { 4012, ("Unknown protocol", "We didn't recognize the protocol you sent.", false) },
                { 4014, ("Disconnected", "Disconnected from voice (kicked, main gateway session dropped, etc.).", false) },
                { 4015, ("Voice server crashed", "The voice server crashed. Try resuming.", true) },
                { 4016, ("Unknown encryption mode", "We didn't recognize your encryption mode.", false) },
                { 4017, ("E2EE required", "End-to-end encryption (DAVE protocol) is required for voice connections.", false) },
                { 4020, ("Bad request", "You sent a malformed request.", false) },
                { 4021, ("Disconnected: Rate Limited", "Disconnected due to rate limit exceeded.", false) },
                { 4022, ("Disconnected: Call Terminated", "Disconnected because the call was terminated (channel deleted, voice server changed, etc.).", false) },
            }.ToFrozenDictionary();

        public static (string Error, string Message) GetErrorCodeMessage(int closeCode)
        {
            if (_closeCodes.TryGetValue(closeCode, out var data))
                return (data.Error, data.Message);

            return ("Unknown error", closeCode.ToString());
        }

        public static bool ShouldReconnect(int closeCode)
        {
            if (_closeCodes.TryGetValue(closeCode, out var data))
                return data.ShouldReconnect;

            return true;
        }
    }
}