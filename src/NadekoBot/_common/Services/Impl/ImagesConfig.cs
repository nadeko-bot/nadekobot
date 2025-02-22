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
        if (data.Version < 5)
        {
            ModifyConfig(c =>
            {
                c.Version = 5;
            });
        }

        if (data.Version < 6)
        {
            ModifyConfig(c =>
            {
                if (c.Slots.Emojis?.Length == 6)
                {
                    c.Slots.Emojis =
                    [
                        new("https://cdn.nadeko.bot/slots/15.png"),
                        ..c.Slots.Emojis
                    ];
                }

                c.Version = 6;
            });
        }
    }
}