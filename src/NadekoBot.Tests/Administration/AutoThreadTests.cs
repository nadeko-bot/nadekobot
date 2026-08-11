using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class AutoThreadTests
{
    private const ulong GUILD_ID = 111;
    private const ulong CHANNEL_ID = 222;

    private TestDbService _db = null!;
    private AutoThreadService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();

        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        _svc = new AutoThreadService(_db, client, new ShardData(client, creds));
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private static IUserMessage Message(string content, int attachments = 0)
    {
        var author = Substitute.For<IGuildUser>();
        author.DisplayName.Returns("Author Name");
        author.Username.Returns("authorname");

        var msg = Substitute.For<IUserMessage>();
        msg.Content.Returns(content);
        msg.Author.Returns(author);
        msg.Attachments.Returns(Enumerable.Range(0, attachments)
                                          .Select(_ => Substitute.For<IAttachment>())
                                          .ToList());
        return msg;
    }

    [Test]
    public void ThreadName_UsesFirstLine_TrimmedAndBounded()
    {
        Assert.That(AutoThreadService.GetThreadName(Message("hello there\nsecond line")),
            Is.EqualTo("hello there"));

        Assert.That(AutoThreadService.GetThreadName(Message("   padded   ")), Is.EqualTo("padded"));

        Assert.That(AutoThreadService.GetThreadName(Message(new string('a', 100))),
            Has.Length.EqualTo(AutoThreadService.MAX_THREAD_NAME_LENGTH));
    }

    [Test]
    public void ThreadName_FallsBackToAuthor_WhenNoText()
    {
        Assert.That(AutoThreadService.GetThreadName(Message("")), Is.EqualTo("Author Name"));
        Assert.That(AutoThreadService.GetThreadName(Message("\n  \n")), Is.EqualTo("Author Name"));
    }

    [Test]
    public void HasMedia_DetectsAttachmentsAndLinks()
    {
        Assert.That(AutoThreadService.HasMedia(Message("look at this", attachments: 1)), Is.True);
        Assert.That(AutoThreadService.HasMedia(Message("see https://example.com/a")), Is.True);
        Assert.That(AutoThreadService.HasMedia(Message("HTTP://EXAMPLE.COM")), Is.True);
        Assert.That(AutoThreadService.HasMedia(Message("just talking")), Is.False);
        Assert.That(AutoThreadService.HasMedia(Message("http is a protocol")), Is.False);
    }

    [Test]
    public async Task Enable_IsIdempotent_AndUpdatesExistingChannel()
    {
        await _svc.EnableAsync(GUILD_ID, CHANNEL_ID, AutoThreadMode.All, AutoThreadArchive.DEFAULT);
        await _svc.EnableAsync(GUILD_ID, CHANNEL_ID, AutoThreadMode.Media, AutoThreadArchive.ONE_WEEK);

        await using var uow = _db.GetDbContext();
        var rows = await uow.GetTable<AutoThreadChannel>().ToListAsyncLinqToDB();

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Mode, Is.EqualTo(AutoThreadMode.Media));
        Assert.That(rows[0].ArchiveDurationMinutes, Is.EqualTo(AutoThreadArchive.ONE_WEEK));
        Assert.That(_svc.IsEnabled(CHANNEL_ID), Is.True);
    }

    [Test]
    public async Task Disable_RemovesRowAndCacheEntry()
    {
        await _svc.EnableAsync(GUILD_ID, CHANNEL_ID, AutoThreadMode.All, AutoThreadArchive.DEFAULT);

        Assert.That(await _svc.DisableAsync(GUILD_ID, CHANNEL_ID), Is.True);
        Assert.That(await _svc.DisableAsync(GUILD_ID, CHANNEL_ID), Is.False);
        Assert.That(_svc.IsEnabled(CHANNEL_ID), Is.False);

        await using var uow = _db.GetDbContext();
        Assert.That(await uow.GetTable<AutoThreadChannel>().CountAsyncLinqToDB(), Is.Zero);
    }

    [Test]
    public void ArchiveDuration_AcceptsOnlyDiscordSupportedValues()
    {
        Assert.That(AutoThreadArchive.TryParse("1h", out var oneHour), Is.True);
        Assert.That(oneHour, Is.EqualTo(AutoThreadArchive.ONE_HOUR));

        Assert.That(AutoThreadArchive.TryParse("7D", out var oneWeek), Is.True);
        Assert.That(oneWeek, Is.EqualTo(AutoThreadArchive.ONE_WEEK));

        Assert.That(AutoThreadArchive.TryParse("2h", out _), Is.False);
        Assert.That(AutoThreadArchive.TryParse("", out _), Is.False);

        Assert.That(AutoThreadService.ToArchiveDuration(AutoThreadArchive.THREE_DAYS),
            Is.EqualTo(ThreadArchiveDuration.ThreeDays));
        Assert.That(AutoThreadService.ToArchiveDuration(AutoThreadArchive.DEFAULT),
            Is.EqualTo(ThreadArchiveDuration.OneDay));
    }
}
