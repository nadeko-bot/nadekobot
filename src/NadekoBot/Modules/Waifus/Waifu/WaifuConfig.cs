using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Waifus.Waifu;

[Cloneable]
public sealed partial class WaifuConfig : ICloneable<WaifuConfig>
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 7;

    [Comment("Minimum price a waifu can have. Default 1000")]
    public long MinPrice { get; set; } = 1_000;

    [Comment("Cost to opt into the waifu system. Default 10000")]
    public long OptInCost { get; set; } = 10_000;

    [Comment("Price decay percentage per cycle for waifus without a manager (0-100). Default 10")]
    public int ManagerlessDecayPercent { get; set; } = 10;

    [Comment("Hours per cycle. Default 24 (1 day)")]
    public double CycleHours { get; set; } = 24.0;

    [Comment("Annual return rate as a decimal. Default 0.17 (17%)")]
    public double BaseReturnRate { get; set; } = 0.17;

    [Comment("Default max backed amount for computing returns. Default 1000000")]
    public long DefaultReturnsCap { get; set; } = 1_000_000;

    [Comment("Hours after cycle start when manager purchases are allowed. Default 18")]
    public int BuyWindowHours { get; set; } = 18;

    [Comment("Base mood points gained from a hug action. Default 50")]
    public int BaseMoodIncrease { get; set; } = 50;

    [Comment("Base food points gained from a nom action. Default 50")]
    public int BaseFoodIncrease { get; set; } = 50;

    [Comment("Max mood/food actions per user per day. Default 2")]
    public int MaxDailyActions { get; set; } = 2;

    [Comment("Max items in a single gift command. Default 100")]
    public int MaxGiftCount { get; set; } = 100;

    [Comment("Premium over current price to buy manager position (0.0-1.0). Default 0.15 (15%)")]
    public double ManagerBuyPremium { get; set; } = 0.15;

    [Comment("Fraction of surplus (bid - old price) paid to the waifu (0.0-1.0). Default 0.50")]
    public double SurplusWaifuShare { get; set; } = 0.50;

    [Comment("Manager's cut of the waifu fee on cycle payouts (0.0-1.0). Default 0.15 (15%)")]
    public double ManagerCutPercent { get; set; } = 0.15;

    [Comment("List of gift items available in the shop")]
    public List<WaifuGiftItemConfig> Items { get; set; } = DefaultItems();

    private static List<WaifuGiftItemConfig> DefaultItems()
        =>
        [
            // Food items
            new() { Id = Guid.Parse("019479a1-0001-7000-8000-000000000001"), Emoji = "🍪", Name = "Cookie", Price = 10, Type = GiftItemType.Food, Effect = 5 },
            new() { Id = Guid.Parse("019479a1-0002-7000-8000-000000000002"), Emoji = "🍩", Name = "Donut", Price = 10, Type = GiftItemType.Food, Effect = 5 },
            new() { Id = Guid.Parse("019479a1-0003-7000-8000-000000000003"), Emoji = "🥖", Name = "Bread", Price = 50, Type = GiftItemType.Food, Effect = 15 },
            new() { Id = Guid.Parse("019479a1-0004-7000-8000-000000000004"), Emoji = "🍙", Name = "Onigiri", Price = 50, Type = GiftItemType.Food, Effect = 15 },
            new() { Id = Guid.Parse("019479a1-0005-7000-8000-000000000005"), Emoji = "🍕", Name = "Pizza", Price = 250, Type = GiftItemType.Food, Effect = 40 },
            new() { Id = Guid.Parse("019479a1-0006-7000-8000-000000000006"), Emoji = "🍔", Name = "Burger", Price = 250, Type = GiftItemType.Food, Effect = 40 },
            new() { Id = Guid.Parse("019479a1-0007-7000-8000-000000000007"), Emoji = "🍱", Name = "Bento", Price = 1000, Type = GiftItemType.Food, Effect = 80 },
            new() { Id = Guid.Parse("019479a1-0008-7000-8000-000000000008"), Emoji = "🍝", Name = "Pasta", Price = 1000, Type = GiftItemType.Food, Effect = 80 },
            new() { Id = Guid.Parse("019479a1-0009-7000-8000-000000000009"), Emoji = "🍰", Name = "Cake", Price = 2500, Type = GiftItemType.Food, Effect = 150 },
            new() { Id = Guid.Parse("019479a1-000a-7000-8000-000000000010"), Emoji = "🍣", Name = "Sushi", Price = 2500, Type = GiftItemType.Food, Effect = 150 },
            new() { Id = Guid.Parse("019479a1-000b-7000-8000-000000000011"), Emoji = "🦞", Name = "Lobster", Price = 5000, Type = GiftItemType.Food, Effect = 250 },
            new() { Id = Guid.Parse("019479a1-000c-7000-8000-000000000012"), Emoji = "🍖", Name = "Feast", Price = 5000, Type = GiftItemType.Food, Effect = 250 },

            // Mood items
            new() { Id = Guid.Parse("019479a1-1001-7000-8000-000000000101"), Emoji = "🌸", Name = "Flower", Price = 10, Type = GiftItemType.Mood, Effect = 5 },
            new() { Id = Guid.Parse("019479a1-1002-7000-8000-000000000102"), Emoji = "🎀", Name = "Ribbon", Price = 10, Type = GiftItemType.Mood, Effect = 5 },
            new() { Id = Guid.Parse("019479a1-1003-7000-8000-000000000103"), Emoji = "🌹", Name = "Rose", Price = 50, Type = GiftItemType.Mood, Effect = 15 },
            new() { Id = Guid.Parse("019479a1-1004-7000-8000-000000000104"), Emoji = "💌", Name = "LoveLetter", Price = 50, Type = GiftItemType.Mood, Effect = 15 },
            new() { Id = Guid.Parse("019479a1-1005-7000-8000-000000000105"), Emoji = "🧸", Name = "Teddy", Price = 250, Type = GiftItemType.Mood, Effect = 40 },
            new() { Id = Guid.Parse("019479a1-1006-7000-8000-000000000106"), Emoji = "🎁", Name = "Gift", Price = 250, Type = GiftItemType.Mood, Effect = 40 },
            new() { Id = Guid.Parse("019479a1-1007-7000-8000-000000000107"), Emoji = "💎", Name = "Diamond", Price = 1000, Type = GiftItemType.Mood, Effect = 80 },
            new() { Id = Guid.Parse("019479a1-1008-7000-8000-000000000108"), Emoji = "👗", Name = "Dress", Price = 1000, Type = GiftItemType.Mood, Effect = 80 },
            new() { Id = Guid.Parse("019479a1-1009-7000-8000-000000000109"), Emoji = "🎹", Name = "Piano", Price = 2500, Type = GiftItemType.Mood, Effect = 150 },
            new() { Id = Guid.Parse("019479a1-100a-7000-8000-000000000110"), Emoji = "🐱", Name = "Kitten", Price = 2500, Type = GiftItemType.Mood, Effect = 150 },
            new() { Id = Guid.Parse("019479a1-100b-7000-8000-000000000111"), Emoji = "🏠", Name = "House", Price = 5000, Type = GiftItemType.Mood, Effect = 250 },
            new() { Id = Guid.Parse("019479a1-100c-7000-8000-000000000112"), Emoji = "🌙", Name = "Moon", Price = 5000, Type = GiftItemType.Mood, Effect = 250 },
        ];
}

[Cloneable]
public sealed partial class WaifuGiftItemConfig : ICloneable<WaifuGiftItemConfig>
{
    public Guid Id { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Price { get; set; }
    public GiftItemType Type { get; set; }
    public int Effect { get; set; }
}
