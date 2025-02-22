namespace NadekoBot.Db.Models;

public class ExcludedItem : DbEntity
{
    public int? XpSettingsId { get; set; }
    public ulong ItemId { get; set; }
    public ExcludedItemType ItemType { get; set; }

    public override int GetHashCode()
        => ItemId.GetHashCode() ^ ItemType.GetHashCode();

    public override bool Equals(object? obj)
        => obj is ExcludedItem ei && ei.ItemId == ItemId && ei.ItemType == ItemType;
}