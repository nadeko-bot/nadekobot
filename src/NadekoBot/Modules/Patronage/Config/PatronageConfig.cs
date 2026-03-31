using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Patronage;

public class PatronageConfig : ConfigServiceBase<PatronConfigData>
{
    public override string Name
        => "patron";

    private static readonly TypedKey<PatronConfigData> _changeKey
        = new("config.patron.updated");

    private const string FILE_PATH = "data/patron.yml";

    public PatronageConfig(IConfigSeria serializer, IPubSub pubSub) : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("enabled",
            static x => x.IsEnabled,
            static (x, v) => x.IsEnabled = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the patronage feature is enabled");

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version == 1)
        {
            ModifyConfig(c =>
            {
                c.Version = 2;
                c.IsEnabled = false;
            });
        }

        if (Data.Version == 2)
        {
            ModifyConfig(c =>
            {
                c.Version = 3;
            });
        }
    }
}