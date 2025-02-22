#nullable disable
namespace NadekoBot.Db.Models;

public class AutoCommand : DbEntity
{
    public string CommandText { get; set; }
    public ulong ChannelId { get; set; }
    public string ChannelName { get; set; }
    public ulong? GuildId { get; set; }
    public string GuildName { get; set; }
    public ulong? VoiceChannelId { get; set; }
    public string VoiceChannelName { get; set; }
    public int Interval { get; set; }
}