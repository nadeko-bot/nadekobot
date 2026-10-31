#nullable enable
using System;
using System.IO;
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

public sealed class MusicAutoplayTests
{
    private const ulong GUILD = 5;

    private sealed class FileDbService : DbService, IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"nadeko_ap_{Guid.NewGuid():N}.db");

        public FileDbService()
        {
            LinqToDBForEFTools.Initialize();
            using var ctx = GetDbContext();
            ctx.Database.EnsureCreated();
        }

        public override NadekoContext GetDbContext()
        {
            var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
            connection.Open();
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

    // A new server repeats the queue, so autoplay must turn repeat off or it never adds a song.
    [Test]
    public async Task QueueAutoPlay_TurnsRepeatOffAndLeavesItOff()
    {
        using var db = new FileDbService();
        using var client = new DiscordSocketClient();
        var service = new MusicService(null!, null!, db, null!, null!, client, null!, null!, null!, null!, null!, null!,
            null!);

        await service.SetRepeatAsync(GUILD, PlayerRepeatType.Queue);

        Assert.That(await service.ToggleQueueAutoPlayAsync(GUILD), Is.True);
        Assert.That(await ReadSettingsInternal(db), Is.EqualTo((true, PlayerRepeatType.None)));

        Assert.That(await service.ToggleQueueAutoPlayAsync(GUILD), Is.False);
        Assert.That(await ReadSettingsInternal(db), Is.EqualTo((false, PlayerRepeatType.None)));
    }

    private static async Task<(bool AutoPlay, PlayerRepeatType Repeat)> ReadSettingsInternal(DbService db)
    {
        await using var ctx = db.GetDbContext();
        var settings = await ctx.GetTable<MusicPlayerSettings>()
                                .FirstAsyncLinqToDB(x => x.GuildId == GUILD);
        return (settings.AutoPlay, settings.PlayerRepeat);
    }
}
