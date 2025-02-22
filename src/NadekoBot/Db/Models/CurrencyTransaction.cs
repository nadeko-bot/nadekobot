#nullable disable
namespace NadekoBot.Db.Models;

public class CurrencyTransaction : DbEntity
{
    public long Amount { get; set; }
    public string Note { get; set; }
    public ulong UserId { get; set; }
    public string Type { get; set; }
    public string Extra { get; set; }
    public ulong? OtherId { get; set; }
}