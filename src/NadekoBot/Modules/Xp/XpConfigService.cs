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
            static conf => conf.TextXpCooldown,
            static (conf, v) => conf.TextXpCooldown = v,
            int.TryParse,
            static (f) => f.ToString("F2"),
            "How often can the users receive XP, in seconds",
            static x => x > 0);

        AddParsedProp("txt.permsg",
            static conf => conf.TextXpPerMessage,
            static (conf, v) => conf.TextXpPerMessage = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "How much XP will the users receive per message",
            static x => x >= 0);

        AddParsedProp("txt.perimage",
            static conf => conf.TextXpFromImage,
            static (conf, v) => conf.TextXpFromImage = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Amount of xp users gain from posting an image",
            static x => x > 0);

        AddParsedProp("voice.perminute",
            static conf => conf.VoiceXpPerMinute,
            static (conf, v) => conf.VoiceXpPerMinute = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Average amount of xp earned per minute in VC",
            static x => x >= 0);

        AddParsedProp("shop.is_enabled",
            static conf => conf.Shop.IsEnabled,
            static (conf, v) => conf.Shop.IsEnabled = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the xp shop is enabled");

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 11)
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