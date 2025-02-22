#nullable disable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NadekoBot.Db.Models;

public class XpSettings : DbEntity
{
    public ulong GuildId { get; set; }
    public bool ServerExcluded { get; set; }
    public HashSet<ExcludedItem> ExclusionList { get; set; } = new();

    public HashSet<XpRoleReward> RoleRewards { get; set; } = new();
    public HashSet<XpCurrencyReward> CurrencyRewards { get; set; } = new();
}