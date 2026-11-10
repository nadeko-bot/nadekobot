namespace NadekoBot.Coordinator
{
    public readonly struct Config
    {
        public int TotalShards { get; init; }
        public int RecheckIntervalMs { get; init; }
        public string ShardStartCommand { get; init; }
        public string ShardStartArgs { get; init; }
        public double UnresponsiveSec { get; init; }

        // Both must be set, and data/api.yml must have a key, or the dashboard API stays off.
        public string ApiAddress { get; init; }
        public int ApiPort { get; init; }
    }
}
