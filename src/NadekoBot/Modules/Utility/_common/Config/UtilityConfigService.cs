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
            c => c.MaxRepeaters,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        AddParsedProp("maxScheduledPerUser",
            c => c.MaxScheduledPerUser,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        AddParsedProp("maxLiveChannels",
            c => c.MaxLiveChannels,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 1)
            ModifyConfig(c => { c.Version = 1; });
    }
}
