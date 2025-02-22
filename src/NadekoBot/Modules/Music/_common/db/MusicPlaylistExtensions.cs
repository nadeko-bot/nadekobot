#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class MusicPlaylistExtensions
{
    public static List<MusicPlaylist> GetPlaylistsOnPage(this DbSet<MusicPlaylist> playlists, int num)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(num, 1);

        return playlists.AsQueryable().Skip((num - 1) * 20).Take(20).Include(pl => pl.Songs).ToList();
    }

    public static MusicPlaylist GetWithSongs(this DbSet<MusicPlaylist> playlists, int id)
        => playlists.Include(mpl => mpl.Songs).FirstOrDefault(mpl => mpl.Id == id);
}