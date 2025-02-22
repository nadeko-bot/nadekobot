#nullable disable
namespace NadekoBot.Db.Models;

public class PlaylistSong : DbEntity
{
    public string Provider { get; set; }
    public MusicType ProviderType { get; set; }
    public string Title { get; set; }
    public string Uri { get; set; }
    public string Query { get; set; }
}

public enum MusicType
{
    Radio,
    YouTube,
    Local,
}