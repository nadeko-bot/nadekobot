#nullable disable
namespace NadekoBot.Db.Models;

public class MusicPlaylist : DbEntity
{
    public string Name { get; set; }
    public string Author { get; set; }
    public ulong AuthorId { get; set; }
    public List<PlaylistSong> Songs { get; set; } = new();
}