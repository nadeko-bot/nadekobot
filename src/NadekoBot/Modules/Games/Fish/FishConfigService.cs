using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Games;

public sealed class FishConfigService : ConfigServiceBase<FishConfig>
{
    private static string FILE_PATH = "data/fish.yml";
    private static readonly TypedKey<FishConfig> _changeKey = new("config.fish.updated");

    public override string Name
        => "fishing";

    public FishConfigService(
        IConfigSeria serializer,
        IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
    }
}