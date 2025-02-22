using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ayu.Discord.Gateway
{
    public static class CloseCodes
    {
        private static IReadOnlyDictionary<int, (string, string)> _closeCodes = new ReadOnlyDictionary<int, (string, string)>(
            new Dictionary<int, (string, string)>()
            {
                { 4000, ("Unknown error", "We're not sure what went wrong. Try reconnecting?")},
                { 4001, ("Unknown opcode", "You sent an invalid Gateway opcode or an invalid payload for an opcode. Don't do that!")},
                { 4002, ("Decode error", "You sent an invalid payload to us. Don't do that!")},
                { 4003, ("Not authenticated", "You sent us a payload prior to identifying.")},
                { 4004, ("Authentication failed", "The account token sent with your identify payload is incorrect.")},
                { 4005, ("Already authenticated", "You sent more than one identify payload. Don't do that!")},
                { 4007, ("Invalid seq", "The sequence sent when resuming the session was invalid. Reconnect and start a new session.")},
                { 4008, ("Rate limited", "Woah nelly! You're sending payloads to us too quickly. Slow it down! You will be disconnected on receiving this.")},
                { 4009, ("Session timed out", "Your session timed out. Reconnect and start a new one.")},
                { 4010, ("Invalid shard", "You sent us an invalid shard when identifying.")},
                { 4011, ("Sharding required", "The session would have handled too many guilds - you are required to shard your connection in order to connect.")},
                { 4012, ("Invalid API version", "You sent an invalid version for the gateway.")},
                { 4013, ("Invalid intent(s)", "You sent an invalid intent for a Gateway Intent. You may have incorrectly calculated the bitwise value.")},
                { 4014, ("Disallowed intent(s)", "You sent a disallowed intent for a Gateway Intent. You may have tried to specify an intent that you have not enabled or are not whitelisted for.")}
            });

        public static (string Error, string Message) GetErrorCodeMessage(int closeCode)
        {
            if (_closeCodes.TryGetValue(closeCode, out var data))
                return data;
            
            return ("Unknown error", closeCode.ToString());
        }
    }
}