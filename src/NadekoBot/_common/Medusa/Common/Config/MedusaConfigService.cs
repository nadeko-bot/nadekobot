using NadekoBot.Common.Configs;

namespace NadekoBot.Medusa;

public sealed class MedusaConfigService : ConfigServiceBase<MedusaConfig>, IMedusaConfigService
{
    private const string FILE_PATH = "data/medusae/medusa.yml";
    private static readonly TypedKey<MedusaConfig> _changeKey = new("config.medusa.updated");

    public override string Name
        => "medusa";

    public MedusaConfigService(
        IConfigSeria serializer,
        IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {   
    }

    public IReadOnlyCollection<string> GetLoadedMedusae()
        => Data.Loaded?.ToList() ?? new List<string>();

    public void AddLoadedMedusa(string name)
    {
        ModifyConfig(conf =>
        {
            if (conf.Loaded is null)
                conf.Loaded = new();
            
            if(!conf.Loaded.Contains(name))
                conf.Loaded.Add(name);
        });
    }
    
    public void RemoveLoadedMedusa(string name)
    {
        ModifyConfig(conf =>
        {
            if (conf.Loaded is null)
                conf.Loaded = new();
            
            conf.Loaded.Remove(name);
        });
    }
}