#nullable disable
namespace NadekoBot.Db.Models;

[ShardFiltered]
public class XpSettings : DbEntity
{
    public const int DEFAULT_FORMULA_A = 9;
    public const int DEFAULT_FORMULA_C = 27;

    public ulong GuildId { get; set; }

    public int XpFormulaA { get; set; } = DEFAULT_FORMULA_A;
    public int XpFormulaC { get; set; } = DEFAULT_FORMULA_C;

    public HashSet<XpRoleReward> RoleRewards { get; set; } = new();
    public HashSet<XpCurrencyReward> CurrencyRewards { get; set; } = new();
}