if (args.Length > 0 && args[0] == "--version")
{
    Console.WriteLine(StatsService.BotVersion);
    return;
}

var shardId = 0;
int? totalShards = null; // 0 to read from creds.yml
if (args.Length > 0 && args[0] != "run")
{
    if (!int.TryParse(args[0], out shardId))
    {
        Console.Error.WriteLine("Invalid first argument (shard id): {0}", args[0]);
        return;
    }

    if (args.Length > 1)
    {
        if (!int.TryParse(args[1], out var shardCount))
        {
            Console.Error.WriteLine("Invalid second argument (total shards): {0}", args[1]);
            return;
        }

        totalShards = shardCount;
    }
}

await new Bot(shardId, totalShards).RunAndBlockAsync();