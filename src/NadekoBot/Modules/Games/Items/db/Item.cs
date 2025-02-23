namespace NadekoBot.Modules.Games.Items.db;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string MediaUrl { get; set; } = string.Empty;
    public ItemQuality Quality { get; set; }
    public bool IsUsable { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string ItemSubtype { get; set; } = string.Empty;
}

public static class ItemTypes
{
    public const string FISHING_POLE = "FISHING_POLE";
}

public enum ItemQuality
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}