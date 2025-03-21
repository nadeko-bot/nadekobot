#nullable disable
using NadekoBot.Common.Configs;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Xp.Services;

public sealed class XpConfigService : ConfigServiceBase<XpConfig>
{
    private const string FILE_PATH = "data/xp.yml";
    private static readonly TypedKey<XpConfig> _changeKey = new("config.xp.updated");

    public override string Name
        => "xp";

    public XpConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("txt.cooldown",
            conf => conf.TextXpCooldown,
            int.TryParse,
            (f) => f.ToString("F2"),
            x => x > 0);

        AddParsedProp("txt.permsg",
            conf => conf.TextXpPerMessage,
            int.TryParse,
            ConfigPrinters.ToString,
            x => x >= 0);

        AddParsedProp("txt.perimage",
            conf => conf.TextXpFromImage,
            int.TryParse,
            ConfigPrinters.ToString,
            x => x > 0);

        AddParsedProp("voice.perminute",
            conf => conf.VoiceXpPerMinute,
            int.TryParse,
            ConfigPrinters.ToString,
            x => x >= 0);

        AddParsedProp("shop.is_enabled",
            conf => conf.Shop.IsEnabled,
            bool.TryParse,
            ConfigPrinters.ToString);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 11)
        {
            ModifyConfig(c => { c.Version = 11; });
        }
    }

    public async Task<bool> AddItemAsync(string uniqueName, XpShopItemType itemType, XpConfig.ShopItemInfo shopItemInfo)
    {
        await Task.Yield();
        
        var success = false;
        ModifyConfig(c =>
        {
            var items = itemType == XpShopItemType.Background
                ? c.Shop.Bgs
                : c.Shop.Frames;

            if (items is not null)
                success = items.TryAdd(uniqueName, shopItemInfo);
        });

        return success;
    }
}