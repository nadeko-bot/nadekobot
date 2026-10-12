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
        IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy)
    {
        AddNumberProp("maxcachesizegb",
            static c => c.MaxCacheSizeGb,
            static (c, v) => c.MaxCacheSizeGb = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum total cache size in gigabytes. Minimum 1. Default 10",
            min: 1,
            max: null, clamp: true);

        AddNumberProp("maxqueuesize",
            static c => c.MaxQueueSize,
            static (c, v) => c.MaxQueueSize = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of tracks in the queue of one server. Default 500",
            min: 1,
            max: 10000);

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 1)
            ModifyConfig(c => { c.Version = 1; });

        if (Data.Version < 2)
            ModifyConfig(c =>
            {
                c.Version = 2;
                c.MaxQueueSize = 500;
            });
    }
}
