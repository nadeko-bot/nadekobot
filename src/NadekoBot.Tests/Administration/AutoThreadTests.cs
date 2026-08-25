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

    private static IUserMessage Message(
        string content,
        int attachments = 0,
        ulong id = 1,
        bool isBot = false,
        bool isWebhook = false,
        bool hasThread = false)
    {
        var author = Substitute.For<IGuildUser>();
        author.DisplayName.Returns("Author Name");
        author.Username.Returns("authorname");
        author.IsBot.Returns(isBot);
        author.IsWebhook.Returns(isWebhook);

        var msg = Substitute.For<IUserMessage>();
        msg.Id.Returns(id);
        msg.Content.Returns(content);
        msg.Author.Returns(author);
        msg.Flags.Returns(hasThread ? MessageFlags.HasThread : MessageFlags.None);
        msg.Attachments.Returns(Enumerable.Range(0, attachments)
                                          .Select(_ => Substitute.For<IAttachment>())
                                          .ToList());
        return msg;
    }

    [Test]
    public void ThreadName_UsesFirstLine_OrFallsBackToAuthor()
    {
        Assert.That(AutoThreadService.GetThreadName(Message("hello there\nsecond line")),
            Is.EqualTo("hello there"));

        Assert.That(AutoThreadService.GetThreadName(Message("   padded   ")), Is.EqualTo("padded"));

        Assert.That(AutoThreadService.GetThreadName(Message(new string('a', 100))),
            Has.Length.EqualTo(AutoThreadService.MAX_THREAD_NAME_LENGTH));

        Assert.That(AutoThreadService.GetThreadName(Message("\n  \n")), Is.EqualTo("Author Name"));
    }

    [Test]
    public void Backfill_SelectsEligibleMessages_NewestFirst_OldestFirstOrder()
    {
        // discord returns newest first
        var messages = new[]
        {
            Message("newest", id: 60),
            Message("bot post", id: 50, isBot: true),
            Message("webhook post", id: 40, isWebhook: true),
            Message("already threaded", id: 30, hasThread: true),
            Message("middle", id: 20),
            Message("oldest", id: 10)
        };

        var targets = AutoThreadService.SelectBackfillTargets(messages, AutoThreadMode.All, 10);
        Assert.That(targets.Select(x => x.Id), Is.EqualTo(new ulong[] { 10, 20, 60 }));

        var capped = AutoThreadService.SelectBackfillTargets(messages, AutoThreadMode.All, 2);
        Assert.That(capped.Select(x => x.Id), Is.EqualTo(new ulong[] { 20, 60 }));
    }

    [Test]
    public void Backfill_MediaMode_KeepsOnlyAttachmentsAndLinks()
    {
        var messages = new[]
        {
            Message("http is a protocol", id: 4),
            Message("plain text", id: 3),
            Message("see HTTPS://EXAMPLE.COM", id: 2),
            Message("with file", attachments: 1, id: 1)
        };

        var targets = AutoThreadService.SelectBackfillTargets(messages, AutoThreadMode.Media, 10);

        Assert.That(targets.Select(x => x.Id), Is.EqualTo(new ulong[] { 1, 2 }));
    }

    [Test]
    public async Task Enable_Overwrites_AndDisable_RemovesChannel()
    {
        await _svc.EnableAsync(GUILD_ID, CHANNEL_ID, AutoThreadMode.All, AutoThreadArchive.DEFAULT);
        await _svc.EnableAsync(GUILD_ID, CHANNEL_ID, AutoThreadMode.Media, AutoThreadArchive.ONE_WEEK);

        await using (var uow = _db.GetDbContext())
        {
            var rows = await uow.GetTable<AutoThreadChannel>().ToListAsyncLinqToDB();

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Mode, Is.EqualTo(AutoThreadMode.Media));
            Assert.That(rows[0].ArchiveDurationMinutes, Is.EqualTo(AutoThreadArchive.ONE_WEEK));
            Assert.That(_svc.IsEnabled(CHANNEL_ID), Is.True);
        }

        Assert.That(await _svc.DisableAsync(GUILD_ID, CHANNEL_ID), Is.True);
        Assert.That(await _svc.DisableAsync(GUILD_ID, CHANNEL_ID), Is.False);
        Assert.That(_svc.IsEnabled(CHANNEL_ID), Is.False);

        await using var uow2 = _db.GetDbContext();
        Assert.That(await uow2.GetTable<AutoThreadChannel>().CountAsyncLinqToDB(), Is.Zero);
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
