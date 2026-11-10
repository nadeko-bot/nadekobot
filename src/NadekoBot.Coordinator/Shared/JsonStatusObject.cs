using System.Collections.Generic;

namespace NadekoBot.Coordinator
{
    // graceful.json of the gRPC coordinator; read once, so an update takes over the running shards.
    public sealed class LegacyCoordState
    {
        public List<JsonStatusObject> StatusObjects { get; init; }
    }

    public sealed class JsonStatusObject
    {
        public int? Pid { get; init; }
        public int GuildCount { get; init; }
        public ConnState ConnectionState { get; init; }
    }
}
