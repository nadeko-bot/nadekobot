using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NadekoBot.Db;
using NadekoBot.Modules.Games.Fish;

namespace NadekoBot.Modules.Games.Fish.Db;

/// <summary>
/// Represents a fishing item owned by a user.
/// </summary>
public class UserFishItem
{
    /// <summary>
    /// The unique identifier for this user fish item.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The ID of the user who owns this item.
    /// </summary>
    public ulong UserId { get; set; }
    
    /// <summary>
    /// The type of the fishing item.
    /// </summary>
    public FishItemType ItemType { get; set; }
    
    /// <summary>
    /// The ID of the fishing item.
    /// </summary>
    public int ItemId { get; set; }
    
    /// <summary>
    /// Indicates whether the item is currently equipped by the user.
    /// </summary>
    public bool IsEquipped { get; set; }
    
    /// <summary>
    /// The number of uses left for this item. Null means unlimited uses.
    /// </summary>
    public int? UsesLeft { get; set; }
    
    /// <summary>
    /// The date and time when this item expires. Null means the item doesn't expire.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }


    public int? ExpiryFromNowInMinutes()
    {
        if (ExpiresAt is null)
            return null;
        
        return (int)(ExpiresAt.Value - DateTime.UtcNow).TotalMinutes;
    }
}

/// <summary>
/// Entity configuration for UserFishItem.
/// </summary>
public class UserFishItemConfiguration : IEntityTypeConfiguration<UserFishItem>
{
    public void Configure(EntityTypeBuilder<UserFishItem> builder)
    {
        builder.HasIndex(x => new { x.UserId });
    }
}