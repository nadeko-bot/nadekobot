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
            x => x.IsEnabled,
            bool.TryParse,
            ConfigPrinters.ToString);

        Migrate();
    }

    private void Migrate()
    {
        ModifyConfig(c =>
        {
            if (c.Version == 1)
            {
                c.Version = 2;
                c.IsEnabled = false;
            }
        });
        
        
        ModifyConfig(c =>
        {
            if (c.Version == 2)
            {
                c.Version = 3;
            }
        });
    }
}