#nullable disable
using CommandLine;

namespace NadekoBot.Modules.Gambling.Common.Events;

public class EventOptions : INadekoCommandOptions
{
    [Option('a', "amount", Required = false, Default = 100, HelpText = "Amount of currency each user receives.")]
    public long Amount { get; set; } = 100;

    [Option('p',
        "pot-size",
        Required = false,
        Default = 0,
        HelpText = "The maximum amount of currency that can be rewarded. 0 means no limit.")]
    public long PotSize { get; set; }

    //[Option('t', "type", Required = false, Default = "reaction", HelpText = "Type of the event. reaction, gamestatus or joinserver.")]
    //public string TypeString { get; set; } = "reaction";
    [Option('d',
        "duration",
        Required = false,
        Default = 24,
        HelpText = "Number of hours the event should run for. Default 24.")]
    public int Hours { get; set; } = 24;


    public void NormalizeOptions()
    {
        if (Amount < 0)
            Amount = 100;
        if (PotSize < 0)
            PotSize = 0;
        if (Hours <= 0)
            Hours = 24;
        if (PotSize != 0 && PotSize < Amount)
            PotSize = 0;
    }
}