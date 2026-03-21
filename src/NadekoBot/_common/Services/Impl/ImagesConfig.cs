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

        if (data.Version < 11)
        {
            ModifyConfig(c =>
            {
                c.Waifu = CreateDefaultWaifuActions();
                c.Version = 11;
            });
        }
    }

    private static ImageUrls.WaifuActionData CreateDefaultWaifuActions()
        => new()
        {
            Hug = Enumerable.Range(0, 20).Select(i => new Uri($"https://cdn.nadeko.bot/w/hug/hug_{i}.gif")).ToArray(),
            Kiss = Enumerable.Range(0, 20).Select(i => new Uri($"https://cdn.nadeko.bot/w/kiss/kiss_{i}.gif")).ToArray(),
            Pat = Enumerable.Range(0, 20).Select(i => new Uri($"https://cdn.nadeko.bot/w/pat/pat_{i}.gif")).ToArray(),
            Nom = Enumerable.Range(0, 20).Select(i => new Uri($"https://cdn.nadeko.bot/w/nom/nom_{i}.gif")).ToArray(),
        };
}