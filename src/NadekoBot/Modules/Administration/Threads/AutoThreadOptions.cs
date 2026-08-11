using CommandLine;

namespace NadekoBot.Modules.Administration;

public class AutoThreadOptions : INadekoCommandOptions
{
    [Option('m',
        "mode",
        Required = false,
        Default = "all",
        HelpText = "Which messages start a thread. all or media. Default all.")]
    public string Mode { get; set; } = "all";

    [Option('a',
        "archive",
        Required = false,
        Default = "24h",
        HelpText = "Inactive time after which the bot archives the thread. 1h, 24h, 3d or 7d. Default 24h.")]
    public string Archive { get; set; } = "24h";

    public void NormalizeOptions()
    {
    }
}
