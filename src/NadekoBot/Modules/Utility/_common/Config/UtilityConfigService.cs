using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Utility;

public sealed class UtilityConfigService : ConfigServiceBase<UtilityConfig>
{
    private static readonly string FILE_PATH = "data/utility.yml";
    private static readonly TypedKey<UtilityConfig> _changeKey = new("config.utility.updated");

    public override string Name
        => "utility";

    public UtilityConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("maxRepeaters",
            static c => c.MaxRepeaters,
            static (c, v) => c.MaxRepeaters = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of repeating messages per server. Default 5",
            static val => val > 0);

        AddParsedProp("maxScheduledPerUser",
            static c => c.MaxScheduledPerUser,
            static (c, v) => c.MaxScheduledPerUser = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of scheduled commands per user per server. Default 5",
            static val => val > 0);

        AddParsedProp("maxLiveChannels",
            static c => c.MaxLiveChannels,
            static (c, v) => c.MaxLiveChannels = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Default maximum number of live channels per server. Default 5",
            static val => val > 0);

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 1)
            ModifyConfig(c => { c.Version = 1; });
    }
}
