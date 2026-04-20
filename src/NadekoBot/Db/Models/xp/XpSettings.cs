#nullable disable
namespace NadekoBot.Db.Models;

[ShardFiltered]
public class XpSettings : DbEntity
{
    public ulong GuildId { get; set; }

    public int XpFormulaA { get; set; } = 9;
    public int XpFormulaC { get; set; } = 27;

    public HashSet<XpRoleReward> RoleRewards { get; set; } = new();
    public HashSet<XpCurrencyReward> CurrencyRewards { get; set; } = new();
}