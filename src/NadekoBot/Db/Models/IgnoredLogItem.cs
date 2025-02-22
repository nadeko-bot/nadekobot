#nullable disable
namespace NadekoBot.Db.Models;

public class IgnoredLogItem : DbEntity
{
    public int LogSettingId { get; set; }
    public LogSetting LogSetting { get; set; }
    public ulong LogItemId { get; set; }
    public IgnoredItemType ItemType { get; set; }
}

public enum IgnoredItemType
{
    Channel,
    User
}