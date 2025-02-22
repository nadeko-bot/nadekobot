#nullable disable
namespace NadekoBot.Db.Models;

public class WaifuItem : DbEntity
{
    public WaifuInfo WaifuInfo { get; set; }
    public int? WaifuInfoId { get; set; }
    public string ItemEmoji { get; set; }
    public string Name { get; set; }
}
