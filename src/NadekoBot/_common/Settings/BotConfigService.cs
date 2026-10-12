using NadekoBot.Common.Configs;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Services;

public sealed class BotConfigService : ConfigServiceBase<BotConfig>
{
    private const string FILE_PATH = "data/bot.yml";
    private static readonly TypedKey<BotConfig> _changeKey = new("config.bot.updated");
    public override string Name { get; } = "bot";

    // The help texts are embed JSON, and an embed description alone can hold 4096 characters.
    public const int MAX_HELP_TEXT_LENGTH = 4000;
    public const int MAX_PREFIX_LENGTH = 10;

    private readonly BotPolicy _policy;

    // The host controls the log style of a managed bot, so the file value does not count there.
    public ConsoleOutputType ConsoleOutputType
        => _policy.Managed ? ConsoleOutputType.Normal : Data.ConsoleOutputType;

    public BotConfigService(IConfigSeria serializer, IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy)
    {
        _policy = policy;
        AddParsedProp("color.ok",
            static bs => bs.Color.Ok,
            static (bs, v) => bs.Color.Ok = v,
            Rgba32.TryParseHex,
            ConfigPrinters.Color,
            "Color used for embed responses when command successfully executes");

        AddParsedProp("color.error",
            static bs => bs.Color.Error,
            static (bs, v) => bs.Color.Error = v,
            Rgba32.TryParseHex,
            ConfigPrinters.Color,
            "Color used for embed responses when command has an error");

        AddParsedProp("color.pending",
            static bs => bs.Color.Pending,
            static (bs, v) => bs.Color.Pending = v,
            Rgba32.TryParseHex,
            ConfigPrinters.Color,
            "Color used for embed responses while command is doing work or is in progress");

        AddTextProp("help.text",
            static bs => bs.HelpText,
            static (bs, v) => bs.HelpText = v,
            MAX_HELP_TEXT_LENGTH,
            "This is the response for the .h command");

        AddTextProp("help.dmtext",
            static bs => bs.DmHelpText,
            static (bs, v) => bs.DmHelpText = v,
            MAX_HELP_TEXT_LENGTH,
            "The string which will be sent whenever someone DMs the bot");

        AddParsedProp("console.type",
            static bs => bs.ConsoleOutputType,
            static (bs, v) => bs.ConsoleOutputType = v,
            Enum.TryParse,
            ConfigPrinters.ToString,
            "Style in which executed commands will show up in the logs");

        AddParsedProp("locale",
            static bs => bs.DefaultLocale,
            static (bs, v) => bs.DefaultLocale = v,
            ConfigParsers.Culture,
            ConfigPrinters.Culture,
            "Default bot language. It has to be in the list of supported languages (.langli)");

        AddParsedProp("timezone",
            static bs => bs.DefaultTimezone,
            static (bs, v) => bs.DefaultTimezone = v,
            ConfigParsers.TimeZoneId,
            ConfigPrinters.TimeZoneId,
            "Default time zone for servers that have not set their own with .timezone (IANA id, e.g. Europe/Berlin). Empty means UTC.",
            type: ConfigPropType.Timezone);

        AddTextProp("prefix",
            static bs => bs.Prefix,
            static (bs, v) => bs.Prefix = v,
            MAX_PREFIX_LENGTH,
            "Which string will be used to recognize the commands",
            required: true);

        AddParsedProp("checkforupdates",
            static bs => bs.CheckForUpdates,
            static (bs, v) => bs.CheckForUpdates = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the bot will check for new releases every hour");

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 2)
            ModifyConfig(c => c.Version = 2);

        if (Data.Version < 3)
        {
            ModifyConfig(c =>
            {
                c.Version = 3;
                c.Blocked.Modules = c.Blocked.Modules.Select(static x
                                         => string.Equals(x,
                                             "ActualCustomReactions",
                                             StringComparison.InvariantCultureIgnoreCase)
                                             ? "ACTUALEXPRESSIONS"
                                             : x)
                                     .Distinct()
                                     .ToHashSet();
            });
        }
        
        if (Data.Version < 4)
            ModifyConfig(c =>
            {
                c.Version = 4;
                c.CheckForUpdates = true;
            });
        
        if (Data.Version < 5)
            ModifyConfig(c =>
            {
                c.Version = 5;
            });
        
        if (Data.Version < 7)
            ModifyConfig(c =>
            {
                c.Version = 7;
                c.IgnoreOtherBots = true;
            });
        
        if (Data.Version < 9)
            ModifyConfig(c =>
            {
                c.Version = 9;
            });

        if (Data.Version < 11)
            ModifyConfig(c =>
            {
                c.Version = 11;
            });

        if (Data.Version < 12)
            ModifyConfig(c =>
            {
                c.Version = 12;
            });
    }
}