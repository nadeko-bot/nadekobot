namespace NadekoBot.Db.Models;

public class XpCurrencyReward : DbEntity
{
    public int XpSettingsId { get; set; }
    public int Level { get; set; }
    public int Amount { get; set; }
}