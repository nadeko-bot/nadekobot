#nullable disable
namespace NadekoBot.Db.Models;

public class RotatingPlayingStatus : DbEntity
{
    public string Status { get; set; }
    public DbActivityType Type { get; set; }
}