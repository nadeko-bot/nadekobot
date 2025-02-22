#nullable disable warnings
using Cloneable;
using NadekoBot.Common.Yml;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Patronage;

namespace NadekoBot.Modules.Xp;

[Cloneable]
public sealed partial class XpConfig : ICloneable<XpConfig>
{
    [Comment("""DO NOT CHANGE""")]
    public int Version { get; set; } = 10;

    [Comment("""How much XP will the users receive per message""")]
    public int TextXpPerMessage { get; set; } = 3;

    [Comment("""How often can the users receive XP, in seconds""")]
    public int TextXpCooldown { get; set; } = 300;

    [Comment("""Amount of xp users gain from posting an image""")]
    public int TextXpFromImage { get; set; } = 3;

    [Comment("""Average amount of xp earned per minute in VC""")]
    public int VoiceXpPerMinute { get; set; } = 3;

    [Comment("""Xp Shop config""")]
    public ShopConfig Shop { get; set; } = new();

    public sealed class ShopConfig
    {
        [Comment("""
                 Whether the xp shop is enabled
                 True -> Users can access the xp shop using .xpshop command
                 False -> Users can't access the xp shop
                 """)]
        public bool IsEnabled { get; set; } = false;

        [Comment("""
                 Which patron tier do users need in order to use the .xpshop bgs command
                 Leave at 'None' if patron system is disabled or you don't want any restrictions
                 """)]
        public PatronTier BgsTierRequirement { get; set; } = PatronTier.None;

        [Comment("""
                 Which patron tier do users need in order to use the .xpshop frames command
                 Leave at 'None' if patron system is disabled or you don't want any restrictions
                 """)]
        public PatronTier FramesTierRequirement { get; set; } = PatronTier.None;

        [Comment("""
                 Frames available for sale. Keys are unique IDs.
                 Do not change keys as they are not publicly visible. Only change properties (name, price, id)
                 Removing a key which previously existed means that all previous purchases will also be unusable.
                 To remove an item from the shop, but keep previous purchases, set the price to -1
                 """)]
        public Dictionary<string, ShopItemInfo>? Frames { get; set; } = new()
        {
            { "default", new() { Name = "No frame", Price = 0, Url = string.Empty } }
        };

        [Comment("""
                 Backgrounds available for sale. Keys are unique IDs. 
                 Do not change keys as they are not publicly visible. Only change properties (name, price, id)
                 Removing a key which previously existed means that all previous purchases will also be unusable.
                 To remove an item from the shop, but keep previous purchases, set the price to -1
                 """)]
        public Dictionary<string, ShopItemInfo>? Bgs { get; set; } = new()
        {
            { "default", new() { Name = "Default Background", Price = 0, Url = string.Empty } }
        };
    }

    public sealed class ShopItemInfo
    {
        [Comment("""Visible name of the item""")]
        public string Name { get; set; }

        [Comment(
            """Price of the item. Set to -1 if you no longer want to sell the item but want the users to be able to keep their old purchase""")]
        public int Price { get; set; }

        [Comment("""Direct url to the .png image which will be applied to the user's XP card""")]
        public string Url { get; set; }

        [Comment("""Optional preview url which will show instead of the real URL in the shop """)]
        public string Preview { get; set; }

        [Comment("""Optional description of the item""")]
        public string Desc { get; set; }
    }
}

public static class XpShopConfigExtensions
{
    public static string? GetItemUrl(this XpConfig.ShopConfig sc, XpShopItemType type, string key)
        => (type switch
        {
            XpShopItemType.Background => sc.Bgs,
            _ => sc.Frames
        })?.TryGetValue(key, out var item) ?? false
            ? item.Url
            : null;
}