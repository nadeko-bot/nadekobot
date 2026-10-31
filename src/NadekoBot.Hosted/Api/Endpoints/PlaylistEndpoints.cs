using System.Diagnostics.CodeAnalysis;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

// Saved playlists are bot-wide, as for .playlists, and .playlistload plays one in any server.
public static class PlaylistEndpoints
{
    public const int MAX_NAME_LENGTH = 20;
    public const int MAX_SONGS = 500;
    public const int MAX_TITLE_LENGTH = 200;
    public const int MAX_LINK_LENGTH = 512;

    private const string WATCH_LINK = "https://youtube.com/watch?v=";
    private const string WATCH_PATH = "/watch";
    private const string VIDEO_QUERY_KEY = "v";
    private const string SHORT_HOST = "youtu.be";

    private static readonly string[] _watchHosts = ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com"];

    public static RouteGroupBuilder MapPlaylistEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/music/playlists", ListPlaylists);
        owner.MapGet("/music/playlists/{id:int}", GetPlaylist);
        owner.MapPost("/music/playlists", CreatePlaylist);
        owner.MapPut("/music/playlists/{id:int}", UpdatePlaylist);
        owner.MapDelete("/music/playlists/{id:int}", DeletePlaylist);
        return owner;
    }

    private static async Task<IResult> ListPlaylists(int? page, DbService db)
    {
        var skip = ApiLimits.Page(page, 0) * MusicService.PLAYLISTS_PER_PAGE;

        await using var ctx = db.GetDbContext();
        var table = ctx.GetTable<MusicPlaylist>();
        var songs = ctx.GetTable<PlaylistSong>();
        var total = await table.CountAsyncLinqToDB();
        var rows = await table
            .OrderByDescending(static x => x.Id)
            .Skip(skip)
            .Take(MusicService.PLAYLISTS_PER_PAGE)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.AuthorId,
                x.Author,
                SongCount = songs.Count(s => Sql.Property<int?>(s, MusicService.PLAYLIST_KEY) == x.Id)
            })
            .ToArrayAsyncLinqToDB();

        var items = new PlaylistInfoDto[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            items[i] = new(r.Id, r.Name ?? string.Empty, r.AuthorId.ToString(), r.Author ?? string.Empty, r.SongCount);
        }

        return Results.Json(new PlaylistPageDto(total, items), ApiJsonContext.Default.PlaylistPageDto);
    }

    private static async Task<IResult> GetPlaylist(int id, DbService db)
    {
        await using var ctx = db.GetDbContext();
        var playlist = await ctx.GetTable<MusicPlaylist>()
            .Where(x => x.Id == id)
            .Select(static x => new { x.Id, x.Name, x.AuthorId, x.Author })
            .FirstOrDefaultAsyncLinqToDB();
        if (playlist is null)
            return ApiErrors.NotFound;

        var songs = await SongsOf(ctx, id)
            .Select(static s => new PlaylistSongDto(s.Title ?? string.Empty, s.Query ?? string.Empty))
            .ToArrayAsyncLinqToDB();

        return Results.Json(
            new PlaylistDto(playlist.Id,
                playlist.Name ?? string.Empty,
                playlist.AuthorId.ToString(),
                playlist.Author ?? string.Empty,
                songs.Length,
                songs),
            ApiJsonContext.Default.PlaylistDto);
    }

    // The music service saves the songs, because only EF knows the key column which links them to the playlist.
    private static async Task<IResult> CreatePlaylist(
        SavePlaylistRequest body,
        IMusicService music,
        DiscordSocketClient client)
    {
        if (!TryReadPlaylist(body, out var name, out var songs) || !TryGetAuthor(client, body.By, out var author))
            return ApiErrors.InvalidValue;

        var id = await music.SavePlaylistAsync(name, author.Id, author.Name, songs);
        if (id is null)
            return ApiErrors.LimitReached;

        var songDtos = new PlaylistSongDto[songs.Count];
        for (var i = 0; i < songs.Count; i++)
            songDtos[i] = new(songs[i].Title, songs[i].Query);

        return Results.Json(
            new PlaylistDto(id.Value, name, author.Id.ToString(), author.Name, songDtos.Length, songDtos),
            ApiJsonContext.Default.PlaylistDto);
    }

    private static async Task<IResult> UpdatePlaylist(int id, SavePlaylistRequest body, DbService db)
    {
        if (!TryReadPlaylist(body, out var name, out var songs))
            return ApiErrors.InvalidValue;

        await using var ctx = db.GetDbContext();
        var playlist = await ctx.Set<MusicPlaylist>()
            .Include(static x => x.Songs)
            .FirstOrDefaultAsyncEF(x => x.Id == id);
        if (playlist is null)
            return ApiErrors.NotFound;

        playlist.Name = name;
        ctx.RemoveRange(playlist.Songs);
        playlist.Songs.Clear();
        playlist.Songs.AddRange(songs);

        try
        {
            await ctx.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another PUT replaced the songs first; only a deleted playlist is not found.
            await using var check = db.GetDbContext();
            return await check.GetTable<MusicPlaylist>().AnyAsyncLinqToDB(x => x.Id == id)
                ? ApiErrors.Create(ApiErrors.CONFLICT, StatusCodes.Status409Conflict)
                : ApiErrors.NotFound;
        }

        return Results.Json(ToDto(playlist), ApiJsonContext.Default.PlaylistDto);
    }

    private static async Task<IResult> DeletePlaylist(int id, DbService db)
    {
        await using var ctx = db.GetDbContext();
        var playlist = await ctx.Set<MusicPlaylist>()
            .Include(static x => x.Songs)
            .FirstOrDefaultAsyncEF(x => x.Id == id);
        if (playlist is null)
            return ApiErrors.NotFound;

        ctx.Remove(playlist);
        try
        {
            await ctx.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiErrors.NotFound;
        }

        return Results.NoContent();
    }

    // Null when the playlist does not exist; the songs are in the order of .playlistshow.
    public static async Task<(string Query, MusicPlatform Platform)[]?> ReadSongQueriesAsync(DbService db, int id)
    {
        await using var ctx = db.GetDbContext();
        if (!await ctx.GetTable<MusicPlaylist>().AnyAsyncLinqToDB(x => x.Id == id))
            return null;

        var rows = await SongsOf(ctx, id)
            .Select(static s => new { s.Query, s.ProviderType })
            .ToArrayAsyncLinqToDB();

        var songs = new (string, MusicPlatform)[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            songs[i] = (rows[i].Query ?? string.Empty, (MusicPlatform)rows[i].ProviderType);

        return songs;
    }

    private static IQueryable<PlaylistSong> SongsOf(NadekoContext ctx, int id)
        => ctx.GetTable<PlaylistSong>()
            .Where(s => Sql.Property<int?>(s, MusicService.PLAYLIST_KEY) == id)
            .OrderBy(static s => s.Id);

    public static bool TryReadPlaylist(
        SavePlaylistRequest body,
        [NotNullWhen(true)] out string? name,
        [NotNullWhen(true)] out List<PlaylistSong>? songs)
    {
        name = body.Name?.Trim();
        songs = null;
        if (name is not { Length: >= 1 and <= MAX_NAME_LENGTH } || body.Songs is not { Length: <= MAX_SONGS } input)
            return false;

        var list = new List<PlaylistSong>(input.Length);
        foreach (var song in input)
        {
            if (song is null
                || !ApiLimits.IsText(song.Title, MAX_TITLE_LENGTH)
                || !TryNormalizeVideoLink(song.Url, out var link))
                return false;

            // As .playlistsave stores a YouTube track.
            list.Add(new PlaylistSong
            {
                Provider = MusicPlatform.Youtube.ToString(),
                ProviderType = MusicType.YouTube,
                Title = song.Title.Trim(),
                Query = link
            });
        }

        songs = list;
        return true;
    }

    // Only a link to one YouTube video, so a saved song never plays another site or a local file.
    public static bool TryNormalizeVideoLink(string? url, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        var text = url?.Trim();
        if (text is null
            || text.Length > MAX_LINK_LENGTH
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.UserInfo.Length > 0
            || !uri.IsDefaultPort)
            return false;

        var path = uri.AbsolutePath.AsSpan();
        if (path.Length > 1 && path[^1] == '/')
            path = path[..^1];

        ReadOnlySpan<char> id;
        var host = uri.IdnHost;
        if (string.Equals(host, SHORT_HOST, StringComparison.OrdinalIgnoreCase))
            id = path[1..];
        else if (IsWatchHostInternal(host) && path.SequenceEqual(WATCH_PATH))
            id = QueryValueInternal(uri.Query, VIDEO_QUERY_KEY);
        else
            return false;

        if (!YoutubeHelpers.IsVideoId(id))
            return false;

        normalized = string.Concat(WATCH_LINK, id);
        return true;
    }

    private static bool IsWatchHostInternal(string host)
    {
        foreach (var h in _watchHosts)
        {
            if (string.Equals(host, h, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static ReadOnlySpan<char> QueryValueInternal(ReadOnlySpan<char> query, ReadOnlySpan<char> key)
    {
        if (query.StartsWith('?'))
            query = query[1..];

        foreach (var range in query.Split('&'))
        {
            var part = query[range];
            if (part.Length > key.Length && part[key.Length] == '=' && part.StartsWith(key, StringComparison.Ordinal))
                return part[(key.Length + 1)..];
        }

        return default;
    }

    // A dashboard user whom the bot does not see becomes the bot, as the API acts as the owner.
    private static bool TryGetAuthor(DiscordSocketClient client, string? by, out (ulong Id, string Name) author)
    {
        author = client.CurrentUser is { } me ? (me.Id, me.Username) : (0, string.Empty);
        if (string.IsNullOrEmpty(by))
            return true;

        if (!ApiLimits.TryParseId(by, out var userId))
            return false;

        if (client.GetUser(userId) is { } user)
            author = (user.Id, user.Username);

        return true;
    }

    private static PlaylistDto ToDto(MusicPlaylist playlist)
    {
        var songs = new PlaylistSongDto[playlist.Songs.Count];
        for (var i = 0; i < songs.Length; i++)
            songs[i] = new(playlist.Songs[i].Title, playlist.Songs[i].Query);

        return new(playlist.Id, playlist.Name, playlist.AuthorId.ToString(), playlist.Author, songs.Length, songs);
    }
}
