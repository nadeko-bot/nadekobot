using NadekoBot.Common.Configs;

namespace NadekoBot.Services;

public sealed class ImagesConfig : ConfigServiceBase<ImageUrls>
{
    private const string PATH = "data/images.yml";

    private static readonly TypedKey<ImageUrls> _changeKey =
        new("config.images.updated");
    
    public override string Name
        => "images";

    public ImagesConfig(IConfigSeria serializer, IPubSub pubSub)
        : base(PATH, serializer, pubSub, _changeKey)
    {
        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 10)
        {
            ModifyConfig(c =>
            {
                if(c.Xp.Bg.ToString().Contains("cdn.nadeko.bot"))
                    c.Xp.Bg = new("https://cdn.nadeko.bot/xp/bgs/v6.png");
                c.Version = 10;
            });
        }
    }
}