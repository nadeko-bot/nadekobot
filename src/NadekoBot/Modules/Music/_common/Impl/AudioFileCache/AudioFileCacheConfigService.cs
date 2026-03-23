using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Music;

public sealed class AudioFileCacheConfigService : ConfigServiceBase<AudioFileCacheConfig>
{
    private const string FILE_PATH = "data/music.yml";
    private static readonly TypedKey<AudioFileCacheConfig> _changeKey = new("config.music.updated");

    public override string Name
        => "music";

    public AudioFileCacheConfigService(
        IConfigSeria serializer,
        IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("maxcachesizegb",
            static c => c.MaxCacheSizeGb,
            int.TryParse,
            ConfigPrinters.ToString,
            static val => val >= 1);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 1)
            ModifyConfig(c => { c.Version = 1; });
    }
}
