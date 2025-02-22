namespace NadekoBot.Coordinator
{
    public readonly struct Config
    {
        public int TotalShards { get; init; }
        public int RecheckIntervalMs { get; init; }
        public string ShardStartCommand { get; init; }
        public string ShardStartArgs { get; init; }
        public double UnresponsiveSec { get; init; }

        public Config(int totalShards, int recheckIntervalMs, string shardStartCommand, string shardStartArgs, double unresponsiveSec)
        {
            TotalShards = totalShards;
            RecheckIntervalMs = recheckIntervalMs;
            ShardStartCommand = shardStartCommand;
            ShardStartArgs = shardStartArgs;
            UnresponsiveSec = unresponsiveSec;
        }

    }
}