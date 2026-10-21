using NadekoBot.Common.Configs;
using NadekoBot.Db.Models;
using System.Globalization;

namespace NadekoBot.Modules.Xp.Services;

public sealed class XpConfigService : ConfigServiceBase<XpConfig>
{
    private const string FILE_PATH = "data/xp.yml";
    private static readonly TypedKey<XpConfig> _changeKey = new("config.xp.updated");

    public override string Name
        => "xp";

    public XpConfigService(IConfigSeria serializer, IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy, UpgradeLegacyYaml)
    {
        AddNumberProp("txt.cooldown",
            static conf => conf.TextXpCooldown,
            static (conf, v) => conf.TextXpCooldown = v,
            int.TryParse,
            static (f) => f.ToString("F2"),
            "How often can the users receive XP, in seconds. 0 turns off xp from messages and images",
            min: 0,
            max: null);

        AddNumberProp("txt.permsg",
            static conf => conf.TextXpPerMessage,
            static (conf, v) => conf.TextXpPerMessage = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "How much XP will the users receive per message",
            min: 0,
            max: null, strict: true);

        AddNumberProp("txt.perimage",
            static conf => conf.TextXpFromImage,
            static (conf, v) => conf.TextXpFromImage = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Amount of xp users gain from posting an image",
            min: 0,
            max: null, strict: true);

        AddNumberProp("voice.perminute",
            static conf => conf.VoiceXpPerMinute,
            static (conf, v) => conf.VoiceXpPerMinute = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Average amount of xp earned per minute in VC",
            min: 0,
            max: null, strict: true);

        AddParsedProp("shop.is_enabled",
            static conf => conf.Shop.IsEnabled,
            static (conf, v) => conf.Shop.IsEnabled = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the xp shop is enabled");

        Migrate();
    }

    // 6.0 renamed the text xp keys and made voice xp a whole number. Before version 7 the cooldown was in minutes.
    public static string UpgradeLegacyYaml(string yaml)
        => LegacyYaml.EditBelowVersion(yaml, 11, static (root, version) =>
        {
            LegacyYaml.Rename(root, "xpPerMessage", "textXpPerMessage");
            if (version < 7)
                LegacyYaml.Rename(root, "messageXpCooldown", "textXpCooldown", static v => ScaleInternal(v, 60));
            else
                LegacyYaml.Rename(root, "messageXpCooldown", "textXpCooldown");

            LegacyYaml.Rename(root, "xpFromImage", "textXpFromImage");
            LegacyYaml.Convert(root, "voiceXpPerMinute", static v => ScaleInternal(v, 1));
        });

    private static string ScaleInternal(string value, int factor)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number))
            return value;

        var scaled = Math.Clamp(Math.Round(number * factor, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
        return ((int)scaled).ToString(CultureInfo.InvariantCulture);
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