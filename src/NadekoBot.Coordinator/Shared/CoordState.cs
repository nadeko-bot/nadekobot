using System;
using System.Collections.Generic;

namespace NadekoBot.Coordinator
{
    // Written after each shard start, so a coordinator which crashed can take over the shards which still run.
    public sealed class CoordState
    {
        public int TotalShards { get; init; }
        public List<SavedShard> Shards { get; init; } = [];
    }

    public sealed class SavedShard
    {
        public int? Pid { get; init; }

        // A pid can belong to another process after a restart of the machine; the start time tells them apart.
        public DateTime? StartedAt { get; init; }

        public string Token { get; init; }
        public int GuildCount { get; init; }
        public ConnState ConnectionState { get; init; }
    }
}
