#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class MusicRepeatTests
{
    private const ulong GUILD = 7;

    private TestDbService _db = null!;
    private DiscordSocketClient _client = null!;
    private MusicService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _client = new DiscordSocketClient();
        _service = new MusicService(null!, null!, _db, null!, null!, _client, null!, null!, null!, null!, null!, null!,
            null!);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _db.Dispose();
    }

    [Test]
    public async Task RepeatAndLoop_ToggleOneSetting_NewServerDoesNotRepeat()
    {
        Assert.That((await _service.GetSavedSettingsAsync(GUILD)).PlayerRepeat, Is.EqualTo(PlayerRepeatType.None));

        await using (var ctx = _db.GetDbContext())
        {
            await ctx.Set<MusicPlayerSettings>().ForGuildAsync(GUILD);
            await ctx.SaveChangesAsync();
        }

        Assert.That(await ReadRepeatInternalAsync(), Is.EqualTo(PlayerRepeatType.None));

        Assert.That(await _service.ToggleRepeatAsync(GUILD, PlayerRepeatType.Track), Is.EqualTo(PlayerRepeatType.Track));
        Assert.That(await ReadRepeatInternalAsync(), Is.EqualTo(PlayerRepeatType.Track));

        Assert.That(await _service.ToggleRepeatAsync(GUILD, PlayerRepeatType.Queue), Is.EqualTo(PlayerRepeatType.Queue));
        Assert.That(await ReadRepeatInternalAsync(), Is.EqualTo(PlayerRepeatType.Queue));

        Assert.That(await _service.ToggleRepeatAsync(GUILD, PlayerRepeatType.Queue), Is.EqualTo(PlayerRepeatType.None));
        Assert.That(await ReadRepeatInternalAsync(), Is.EqualTo(PlayerRepeatType.None));
        Assert.That((await _service.GetSavedSettingsAsync(GUILD)).PlayerRepeat, Is.EqualTo(PlayerRepeatType.None));
    }

    [Test]
    public void Queue_NextFollowsRepeat_AndPreviousWrapsFromFirstToLast()
    {
        var repeating = new MusicQueue();
        Assert.That(repeating.GetNext(true), Is.Null);

        repeating.Enqueue(Track("A"), "q", out _);
        Assert.That(repeating.GetNext(true), Is.Null, "the only track is already playing");

        repeating.Enqueue(Track("B"), "q", out _);
        Assert.That(repeating.GetNext(false)?.Title, Is.EqualTo("B"));

        repeating.Advance();
        Assert.That(repeating.GetNext(false), Is.Null);
        Assert.That(repeating.GetNext(true)?.Title, Is.EqualTo("A"));

        var queue = new MusicQueue();
        Assert.That(queue.TrySetPrevious(out _), Is.False);

        queue.Enqueue(Track("a"), "u", out _);
        Assert.That(queue.TrySetPrevious(out var only), Is.True);
        Assert.That(only, Is.EqualTo(0));

        queue.Enqueue(Track("b"), "u", out _);
        queue.Enqueue(Track("c"), "u", out _);

        Assert.That(queue.TrySetPrevious(out var wrapped), Is.True);
        Assert.That(wrapped, Is.EqualTo(2));
        Assert.That(queue.TrySetPrevious(out var back), Is.True);
        Assert.That(back, Is.EqualTo(1));
        Assert.That(queue.GetCurrent(out _)?.Title, Is.EqualTo("b"));
    }

    private static ITrackInfo Track(string title)
        => new RemoteTrackInfo(title, title, "https://example.com/" + title, string.Empty, System.TimeSpan.Zero,
            MusicPlatform.Youtube);

    private async Task<PlayerRepeatType> ReadRepeatInternalAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<MusicPlayerSettings>()
                        .Where(x => x.GuildId == GUILD)
                        .Select(x => x.PlayerRepeat)
                        .FirstAsyncLinqToDB();
    }
}
