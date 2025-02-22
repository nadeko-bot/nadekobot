namespace NadekoBot.Db.Models;

public class TempRole
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public bool Remove { get; set; }
    public ulong RoleId { get; set; }
    public ulong UserId { get; set; }
    
    public DateTime ExpiresAt { get; set; }
}