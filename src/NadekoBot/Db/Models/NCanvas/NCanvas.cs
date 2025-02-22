using System.ComponentModel.DataAnnotations;

namespace NadekoBot.Db.Models;

public class NCPixel
{
    [Key]
    public int Id { get; set; }
    
    public required int Position { get; init; }

    public required long Price { get; init; }

    public required ulong OwnerId { get; init; }
    public required uint Color { get; init; }

    [MaxLength(256)]
    public required string Text { get; init; }
}