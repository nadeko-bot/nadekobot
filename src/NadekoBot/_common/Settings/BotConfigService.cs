using NadekoBot.Common.Configs;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Services;

public sealed class BotConfigService : ConfigServiceBase<BotConfig>
{
    private const string FILE_PATH = "data/bot.yml";
    private static readonly TypedKey<BotConfig> _changeKey = new("config.bot.updated");
    public override string Name { get; } = "bot";

    public BotConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
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

        AddParsedProp("help.text",
            static bs => bs.HelpText,
            static (bs, v) => bs.HelpText = v,
            ConfigParsers.String,
            ConfigPrinters.ToString,
            "This is the response for the .h command");

        AddParsedProp("help.dmtext",
            static bs => bs.DmHelpText,
            static (bs, v) => bs.DmHelpText = v,
            ConfigParsers.String,
            ConfigPrinters.ToString,
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

        AddParsedProp("prefix",
            static bs => bs.Prefix,
            static (bs, v) => bs.Prefix = v,
            ConfigParsers.String,
            ConfigPrinters.ToString,
            "Which string will be used to recognize the commands");

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
                RemoveBareCommandKeys(c.Blocked);
                RemoveBareCommandKeys(c.DmBlocked);
            });
    }

    private static void RemoveBareCommandKeys(BlockedConfig blocked)
        => blocked.Commands.RemoveWhere(static x => CommandKeyMigration.BareSubcommandKeys.Contains(x));
}