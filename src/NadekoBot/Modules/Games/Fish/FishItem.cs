namespace NadekoBot.Modules.Games;

/// <summary>
/// Represents an item used in the fishing game.
/// </summary>
public class FishItem
{
    /// <summary>
    /// Unique identifier for the item.
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// Type of the fishing item (pole, bait, boat, potion).
    /// </summary>
    public FishItemType ItemType { get; set; }
    
    /// <summary>
    /// Name of the item.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Item Emoji
    /// </summary>
    public string Emoji { get; set; } = string.Empty;
    
    /// <summary>
    /// Description of the item.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Price of the item.
    /// </summary>
    public int Price { get; set; }
    
    /// <summary>
    /// Number of times the item can be used. Null means unlimited uses.
    /// </summary>
    public int? Uses { get; set; }
    
    /// <summary>
    /// Duration of the item's effect in minutes. Null means permanent effect.
    /// </summary>
    public int? DurationMinutes { get; set; }
    
    /// <summary>
    /// Multiplier affecting the fish catch rate.
    /// </summary>
    public double? FishMultiplier { get; set; }
    
    /// <summary>
    /// Multiplier affecting the trash catch rate.
    /// </summary>
    public double? TrashMultiplier { get; set; }
    
    /// <summary>
    /// Multiplier affecting the maximum star rating of caught fish.
    /// </summary>
    public double? MaxStarMultiplier { get; set; }
    
    /// <summary>
    /// Multiplier affecting the chance of catching rare fish.
    /// </summary>
    public double? RareMultiplier { get; set; }
    
    /// <summary>
    /// Multiplier affecting the fishing speed.
    /// </summary>
    public double? FishingSpeedMultiplier { get; set; }
}

/// <summary>
/// Defines the types of items available in the fishing game.
/// </summary>
public enum FishItemType
{
    /// <summary>
    /// Fishing pole used to catch fish.
    /// </summary>
    Pole,
    
    /// <summary>
    /// Bait used to attract fish.
    /// </summary>
    Bait,
    
    /// <summary>
    /// Boat used for fishing.
    /// </summary>
    Boat,
    
    /// <summary>
    /// Potion that provides temporary effects.
    /// </summary>
    Potion
}