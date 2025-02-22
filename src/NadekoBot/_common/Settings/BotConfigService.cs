#nullable disable
using NadekoBot.Common.Configs;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Services;

/// <summary>
///     Settings service for bot-wide configuration.
/// </summary>
public sealed class BotConfigService : ConfigServiceBase<BotConfig>
{
    private const string FILE_PATH = "data/bot.yml";
    private static readonly TypedKey<BotConfig> _changeKey = new("config.bot.updated");
    public override string Name { get; } = "bot";

    public BotConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("color.ok", bs => bs.Color.Ok, Rgba32.TryParseHex, ConfigPrinters.Color);
        AddParsedProp("color.error", bs => bs.Color.Error, Rgba32.TryParseHex, ConfigPrinters.Color);
        AddParsedProp("color.pending", bs => bs.Color.Pending, Rgba32.TryParseHex, ConfigPrinters.Color);
        AddParsedProp("help.text", bs => bs.HelpText, ConfigParsers.String, ConfigPrinters.ToString);
        AddParsedProp("help.dmtext", bs => bs.DmHelpText, ConfigParsers.String, ConfigPrinters.ToString);
        AddParsedProp("console.type", bs => bs.ConsoleOutputType, Enum.TryParse, ConfigPrinters.ToString);
        AddParsedProp("locale", bs => bs.DefaultLocale, ConfigParsers.Culture, ConfigPrinters.Culture);
        AddParsedProp("prefix", bs => bs.Prefix, ConfigParsers.String, ConfigPrinters.ToString);
        AddParsedProp("checkforupdates", bs => bs.CheckForUpdates, bool.TryParse, ConfigPrinters.ToString);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 2)
            ModifyConfig(c => c.Version = 2);

        if (data.Version < 3)
        {
            ModifyConfig(c =>
            {
                c.Version = 3;
                c.Blocked.Modules = c.Blocked.Modules?.Select(static x
                                         => string.Equals(x,
                                             "ActualCustomReactions",
                                             StringComparison.InvariantCultureIgnoreCase)
                                             ? "ACTUALEXPRESSIONS"
                                             : x)
                                     .Distinct()
                                     .ToHashSet();
            });
        }
        
        if (data.Version < 4)
            ModifyConfig(c =>
            {
                c.Version = 4;
                c.CheckForUpdates = true;
            });
        
        if(data.Version < 5)
            ModifyConfig(c =>
            {
                c.Version = 5;
            });
        
        if(data.Version < 7)
            ModifyConfig(c =>
            {
                c.Version = 7;
                c.IgnoreOtherBots = true;
            });
        
        if(data.Version < 9)
            ModifyConfig(c =>
            {
                c.Version = 9;
            });
    }
}