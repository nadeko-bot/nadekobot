using NadekoBot.Migrate;

const string USAGE = """
                     Usage:
                       NadekoBot.Migrate db [path to NadekoBot.db]   updates the database (without a path: the one in data/creds.yml)
                       NadekoBot.Migrate configs                     updates data/creds.yml and data/*.yml in the current folder
                     Without a path, run it in the bot's folder, which has data/ and Migrations/.
                     """;

switch (args)
{
    case ["db"]:
        Environment.ExitCode = await MigrateCommand.RunAsync(null);
        break;
    case ["db", var path] when path.Length > 0 && path[0] != '-':
        // The tool's own folder always has the Migrations/ of the bot it was built with.
        var fullPath = Path.GetFullPath(path);
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        Environment.ExitCode = await MigrateCommand.RunAsync(fullPath);
        break;
    case ["configs"]:
        Environment.ExitCode = MigrateCommand.RunConfigs();
        break;
    default:
        Console.Error.WriteLine(USAGE);
        Environment.ExitCode = MigrateCommand.EXIT_FAILED;
        break;
}
