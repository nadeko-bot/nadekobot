using NadekoBot.Hosted;

if (args is ["--version", ..])
{
    Console.WriteLine(StatsService.BotVersion);
    return;
}

if (!HostArgs.TryParse(args, out var parsed, out var error))
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = HostedProgram.EXIT_CONFIG_ERROR;
    return;
}

// Bot services run loops on foreground threads, which would keep the process alive after the web host stops.
int code;
try
{
    code = await HostedProgram.RunAsync(parsed);
}
catch (Exception ex)
{
    // For example the API port in use: the log must say why the supervisor keeps restarting the process.
    Log.Fatal(ex, "The host failed");
    code = 1;
}

HostedProgram.FlushAndExit(code);
