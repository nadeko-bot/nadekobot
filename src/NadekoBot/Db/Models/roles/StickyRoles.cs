namespace NadekoBot.Db.Models;

#nullable disable
public class StickyRole : DbEntity
{
    public ulong GuildId { get; set; }
    public string RoleIds { get; set; }
    public ulong UserId { get; set; }

    public ulong[] GetRoleIds()
        => string.IsNullOrWhiteSpace(RoleIds)
            ? []
            : RoleIds.Split(',').Select(ulong.Parse).ToArray();
}