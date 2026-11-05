if (args.Length > 0 && args[0] == "--version")
{
    Console.WriteLine(StatsService.BotVersion);
    return;
}

if (args.Length > 0 && args[0] == "--commandlist")
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: NadekoBot --commandlist <path>");
        Environment.ExitCode = 1;
        return;
    }

    await Bot.WriteCommandListAsync(args[1]);
    // Some services (a Redis connection, for example) keep threads alive, so the process ends here.
    Environment.Exit(0);
}

var shardId = 0;
int? totalShards = null; // 0 to read from creds.yml
if (args.Length > 0 && args[0] != "run")
{
    if (!int.TryParse(args[0], out shardId))
    {
        Console.Error.WriteLine("Invalid first argument (shard id): {0}", args[0]);
        Environment.ExitCode = 1;
        return;
    }

    if (args.Length > 1)
    {
        if (!int.TryParse(args[1], out var shardCount))
        {
            Console.Error.WriteLine("Invalid second argument (total shards): {0}", args[1]);
            Environment.ExitCode = 1;
            return;
        }

        totalShards = shardCount;
    }
}

await (await Bot.CreateAsync(shardId, totalShards)).RunAndBlockAsync();