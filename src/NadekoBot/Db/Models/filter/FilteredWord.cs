namespace NadekoBot.Db.Models;

public class FilteredWord : DbEntity
{
    public int? GuildFilterConfigId { get; set; }
    public string? Word { get; set; }

    public override bool Equals(object? obj) => obj is FilteredWord fw && fw.Word == Word;
    
    public override int GetHashCode() => Word?.GetHashCode() ?? 0;
}