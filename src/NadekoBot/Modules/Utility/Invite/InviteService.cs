#nullable disable
using CommandLine;

namespace NadekoBot.Modules.Utility.Services;

public class InviteService : INService
{
    public class Options : INadekoCommandOptions
    {
        [Option('m',
            "max-uses",
            Required = false,
            Default = 0,
            HelpText = "Maximum number of times the invite can be used. Default 0 (never).")]
        public int MaxUses { get; set; }

        [Option('u',
            "unique",
            Required = false,
            Default = false,
            HelpText =
                "Not setting this flag will result in bot getting the existing invite with the same settings if it exists, instead of creating a new one.")]
        public bool Unique { get; set; } = false;

        [Option('t',
            "temporary",
            Required = false,
            Default = false,
            HelpText = "If this flag is set, the user will be kicked from the guild once they close their client.")]
        public bool Temporary { get; set; } = false;

        [Option('e',
            "expire",
            Required = false,
            Default = 0,
            HelpText = "Time in seconds to expire the invite. Default 0 (no expiry).")]
        public int Expire { get; set; }

        public void NormalizeOptions()
        {
            if (MaxUses < 0)
                MaxUses = 0;

            if (Expire < 0)
                Expire = 0;
        }
    }
}