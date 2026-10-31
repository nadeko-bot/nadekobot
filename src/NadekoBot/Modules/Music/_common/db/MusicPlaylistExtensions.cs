#nullable disable
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;

namespace NadekoBot.Db;

public static class MusicPlaylistExtensions
{
    public static MusicPlaylist GetWithSongs(this IQueryable<MusicPlaylist> playlists, int id)
        => playlists.Include(mpl => mpl.Songs).FirstOrDefault(mpl => mpl.Id == id);
}