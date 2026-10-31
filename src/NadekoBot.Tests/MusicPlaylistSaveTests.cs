#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Services;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class MusicPlaylistSaveTests
{
    private const ulong AUTHOR = 10;
    private const ulong OTHER_AUTHOR = 11;
    private const int EXTRA_SAVES = 5;
    private const int SONGS_PER_PLAYLIST = 2;

    // One connection for each context, as on the real bot, so parallel saves really race.
    private sealed class FileDbService : DbService, IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"nadeko_pl_{Guid.NewGuid():N}.db");

        public FileDbService()
        {
            LinqToDBForEFTools.Initialize();
            using var ctx = GetDbContext();
            ctx.Database.EnsureCreated();
            ctx.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        }

        // The bot's pragmas, so the saves meet the same locks as in production.
        public override NadekoContext GetDbContext()
        {
            var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";
            cmd.ExecuteNonQuery();
            return new NadekoContext(connection);
        }

        public override Task SetupAsync()
            => Task.CompletedTask;

        public override string ConnectionString
            => $"Data Source={_path}";

        public void Dispose()
        {
            foreach (var file in new[] { _path, _path + "-wal", _path + "-shm", _path + "-journal" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    private static PlaylistSong[] Songs(int playlist)
        => Enumerable.Range(0, SONGS_PER_PLAYLIST)
            .Select(i => new PlaylistSong
            {
                Provider = nameof(MusicPlatform.Youtube),
                ProviderType = MusicType.YouTube,
                Title = $"{playlist}-{i}",
                Query = $"https://youtube.com/watch?v={playlist:D5}abcd{i}"
            })
            .ToArray();

    [Test]
    public async Task Save_KeepsTheCapForEachMemberAlsoWhenSavesRunAtOnce()
    {
        using var db = new FileDbService();
        using var client = new DiscordSocketClient();
        var service = new MusicService(null!, null!, db, null!, null!, client, null!, null!, null!, null!, null!, null!, null!);

        const int attempts = MusicService.MAX_PLAYLISTS_PER_USER + EXTRA_SAVES;
        var ids = await Task.WhenAll(Enumerable.Range(0, attempts)
            .Select(i => Task.Run(() => service.SavePlaylistAsync($"pl{i}", AUTHOR, "author", Songs(i)))));

        Assert.That(ids.Count(static id => id is not null), Is.EqualTo(MusicService.MAX_PLAYLISTS_PER_USER));
        Assert.That(await service.SavePlaylistAsync("other", OTHER_AUTHOR, "other", Songs(attempts)), Is.Not.Null);

        await using var ctx = db.GetDbContext();
        Assert.That(await ctx.GetTable<MusicPlaylist>().CountAsyncLinqToDB(p => p.AuthorId == AUTHOR),
            Is.EqualTo(MusicService.MAX_PLAYLISTS_PER_USER));

        foreach (var id in ids.Where(static id => id is not null))
        {
            var titles = await ctx.GetTable<PlaylistSong>()
                .Where(s => Sql.Property<int?>(s, "MusicPlaylistId") == id)
                .Select(static s => s.Title)
                .ToArrayAsyncLinqToDB();
            Assert.That(titles, Has.Length.EqualTo(SONGS_PER_PLAYLIST));
        }

        Assert.That(await ctx.GetTable<PlaylistSong>().CountAsyncLinqToDB(),
            Is.EqualTo((MusicService.MAX_PLAYLISTS_PER_USER + 1) * SONGS_PER_PLAYLIST),
            "a refused save stores no songs");
    }
    [Test]
    public async Task PlaylistsPage_ListsOneMembersPlaylistsInOrderWithSongCounts()
    {
        using var db = new FileDbService();
        using var client = new DiscordSocketClient();
        var service = new MusicService(null!, null!, db, null!, null!, client, null!, null!, null!, null!, null!, null!, null!);

        const int mine = MusicService.PLAYLISTS_PER_PAGE + 1;
        var ids = new int[mine];
        for (var i = 0; i < mine; i++)
        {
            await service.SavePlaylistAsync($"other{i}", OTHER_AUTHOR, "other", Songs(100 + i));
            ids[i] = (await service.SavePlaylistAsync($"pl{i}", AUTHOR, "author", Songs(i)))!.Value;
        }

        var first = await service.GetPlaylistsPageAsync(AUTHOR, 0);
        var second = await service.GetPlaylistsPageAsync(AUTHOR, 1);
        var all = first.Concat(second).ToArray();

        Assert.That(first, Has.Count.EqualTo(MusicService.PLAYLISTS_PER_PAGE));
        Assert.That(all.Select(static p => p.Id), Is.EqualTo(ids));
        Assert.That(all, Has.All.Matches<SavedPlaylistInfo>(p => p.Author == "author" && p.SongCount == SONGS_PER_PLAYLIST));
        Assert.That(await service.GetPlaylistsPageAsync(AUTHOR, 2), Is.Empty);
        Assert.That(await service.GetPlaylistsPageAsync(null, 0), Has.Count.EqualTo(MusicService.PLAYLISTS_PER_PAGE));
    }
}
