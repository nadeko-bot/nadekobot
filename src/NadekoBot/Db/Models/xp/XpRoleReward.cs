namespace NadekoBot.Db.Models;

public class XpRoleReward : DbEntity
{
    public int XpSettingsId { get; set; }
    public int Level { get; set; }
    public ulong RoleId { get; set; }

    /// <summary>
    ///     Whether the role should be removed (true) or added (false)
    /// </summary>
    public bool Remove { get; set; }
}