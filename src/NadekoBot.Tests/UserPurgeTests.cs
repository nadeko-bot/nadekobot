#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NadekoBot.Db;
using NadekoBot.Db.Models;
using NadekoBot.Db.UserData;
using NadekoBot.Modules.Owner.UserData;
using NadekoBot.Modules.Utility.LineUp;
using NadekoBot.Modules.Utility.Scheduled;
using NadekoBot.Modules.Waifus.Waifu.Db;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class UserPurgeTests
{
    private const ulong ALICE = 1;
    private const ulong BOB = 2;
    private const ulong CAROL = 3;
    private const int GIFTS_TO_BOB = (UserPurgeService.BATCH_SIZE * 2) + 200;

    private TestDbService _db = null!;
    private UserPurgeService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _svc = new UserPurgeService(_db);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    // A new text column on a table linked to a member must be removed or kept on purpose.
    [Test]
    public void Registry_CoversEveryTextColumnOfRowsLinkedToAMember()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var ctx = new NadekoContext(conn);

        // Entity types which share a table add up to its columns.
        var entities = ctx.Model.GetEntityTypes()
            .Where(static e => e.GetTableName() is not null)
            .GroupBy(static e => e.GetTableName()!)
            .ToDictionary(static g => g.Key, static g => g.SelectMany(static e => e.GetProperties()).ToList());

        var deleted = UserDataColumns.Changes.Where(static c => c.Set is null).Select(static c => c.Table).ToHashSet();
        var changed = UserDataColumns.Changes
            .Where(static c => c.Set is not null)
            .SelectMany(static c => c.Set!.Select(s => $"{c.Table}.{s.Column}"))
            .ToHashSet();

        foreach (var (table, props) in entities)
        {
            if (!props.Any(static p => UserDataColumns.UserColumns.Contains(p.GetColumnName())) || deleted.Contains(table))
                continue;

            foreach (var p in props.Where(static p => p.ClrType == typeof(string)))
            {
                var key = $"{table}.{p.GetColumnName()}";
                Assert.That(changed.Contains(key) || UserDataColumns.Kept.ContainsKey(key), Is.True, key);
            }
        }

        foreach (var change in UserDataColumns.Changes)
        {
            Assert.That(entities.ContainsKey(change.Table), Is.True, change.Table);
            var props = entities[change.Table];

            // A Discord user id, not the int key of a DiscordUser row.
            var userColumn = props.FirstOrDefault(p => p.GetColumnName() == change.UserColumn);
            Assert.That(Nullable.GetUnderlyingType(userColumn?.ClrType ?? typeof(object)) ?? userColumn?.ClrType,
                Is.EqualTo(typeof(ulong)), $"{change.Table}.{change.UserColumn}");

            foreach (var (column, value) in change.Set ?? [])
            {
                var prop = props.FirstOrDefault(p => p.GetColumnName() == column);
                Assert.That(prop, Is.Not.Null, $"{change.Table}.{column}");
                if (value is null)
                    Assert.That(prop!.IsNullable, Is.True, $"{change.Table}.{column} is not nullable");
            }
        }

        foreach (var key in UserDataColumns.Kept.Keys)
        {
            var parts = key.Split('.');
            Assert.That(entities.TryGetValue(parts[0], out var props)
                        && props.Any(p => p.GetColumnName() == parts[1]), Is.True, key);
            Assert.That(changed, Does.Not.Contain(key), key);
        }
    }

    [Test]
    public async Task Purge_RemovesNamesAndPrivateTextAndKeepsEveryOtherRecord()
    {
        await SeedInternal();

        var changed = await _svc.PurgeAsync(ALICE);

        await using var ctx = _db.GetDbContext();
        var name = UserDataColumns.NAME;

        var alice = await ctx.GetTable<DiscordUser>().FirstAsyncLinqToDB(x => x.UserId == ALICE);
        Assert.That((alice.Username, alice.AvatarId, alice.CurrencyAmount, alice.TotalXp), Is.EqualTo((name, (string?)null, 500L, 70L)));
        Assert.That((await ctx.GetTable<DiscordUser>().FirstAsyncLinqToDB(x => x.UserId == BOB)).Username, Is.EqualTo("bob"));

        var quotes = await ctx.GetTable<Quote>().OrderBy(x => x.AuthorId).ToListAsyncLinqToDB();
        Assert.That(quotes.Select(static q => (q.AuthorName, q.Text)), Is.EqualTo(new[] { (name, "a quote"), ("bob", "b quote") }));
        Assert.That((await ctx.GetTable<MusicPlaylist>().FirstAsyncLinqToDB()).Author, Is.EqualTo(name));

        // Every transfer which shows Alice's name or message is scrubbed, over several batches. No row goes away,
        // and the name and message of a member who sent something to Alice stay.
        var txs = ctx.GetTable<CurrencyTransaction>();
        Assert.That(await txs.CountAsyncLinqToDB(), Is.EqualTo(GIFTS_TO_BOB + 7));
        Assert.That(await txs.CountAsyncLinqToDB(x => x.Extra == "alice" || x.Note == "from alice"), Is.Zero);
        Assert.That(await txs.CountAsyncLinqToDB(x => x.OtherId == ALICE && x.Extra == name && x.Note == null), Is.EqualTo(GIFTS_TO_BOB + 1));
        Assert.That(await txs.CountAsyncLinqToDB(x => x.Note == "for alice" && x.Extra == "bob"), Is.EqualTo(2));
        Assert.That((await txs.FirstAsyncLinqToDB(x => x.UserId == ALICE && x.OtherId == CAROL)).Note, Is.EqualTo("reason"));
        Assert.That((await txs.FirstAsyncLinqToDB(x => x.Type == "blackjack")).Extra, Is.EqualTo("bet"));
        Assert.That((await txs.FirstAsyncLinqToDB(x => x.UserId == BOB && x.OtherId == CAROL)).Extra, Is.EqualTo("bob"));

        // The claim, the manager and the price stay, only the profile text goes.
        var waifu = await ctx.GetTable<WaifuInfo>().FirstAsyncLinqToDB(x => x.UserId == ALICE);
        Assert.That((waifu.Description, waifu.Quote, waifu.ManagerUserId, waifu.Price), Is.EqualTo(((string?)null, (string?)null, (ulong?)BOB, 1000L)));

        var lineUp = await ctx.GetTable<LineUpUser>().FirstAsyncLinqToDB(x => x.UserId == ALICE);
        Assert.That(lineUp.Reason, Is.Null);

        Assert.That(await ctx.GetTable<TodoModel>().Select(static x => x.UserId).ToListAsyncLinqToDB(), Is.EqualTo(new[] { BOB }));
        Assert.That(await ctx.GetTable<ArchivedTodoListModel>().CountAsyncLinqToDB(), Is.Zero);
        Assert.That(await ctx.GetTable<Reminder>().Select(static x => x.UserId).ToListAsyncLinqToDB(), Is.EqualTo(new[] { BOB }));
        Assert.That(await ctx.GetTable<ScheduledCommand>().Select(static x => x.UserId).ToListAsyncLinqToDB(), Is.EqualTo(new[] { BOB }));

        Assert.That((await ctx.GetTable<Warning>().FirstAsyncLinqToDB(x => x.UserId == ALICE)).Reason, Is.EqualTo("spam"));
        Assert.That((await ctx.GetTable<UserXpStats>().FirstAsyncLinqToDB(x => x.UserId == ALICE)).Xp, Is.EqualTo(70));
        Assert.That((await ctx.GetTable<BankUser>().FirstAsyncLinqToDB(x => x.UserId == ALICE)).Balance, Is.EqualTo(900));

        // User row, quote, playlist, the gifts and the award from Alice, Alice's own gift, waifu, line-up, two todos,
        // the archive, the reminder, the scheduled command.
        Assert.That(changed, Is.EqualTo(1 + 1 + 1 + GIFTS_TO_BOB + 1 + 1 + 1 + 1 + 2 + 1 + 1 + 1));
    }

    private async Task SeedInternal()
    {
        await using var ctx = _db.GetDbContext();

        foreach (var (id, name) in new[] { (ALICE, "alice"), (BOB, "bob") })
        {
            await ctx.GetTable<DiscordUser>().InsertAsync(() => new DiscordUser
            {
                UserId = id, Username = name, AvatarId = "avatar", TotalXp = 70, CurrencyAmount = 500, IsClubAdmin = false
            });
        }

        await ctx.GetTable<Quote>().InsertAsync(() => new Quote
        {
            GuildId = 9, Keyword = "K", AuthorName = "alice", AuthorId = ALICE, Text = "a quote"
        });
        await ctx.GetTable<Quote>().InsertAsync(() => new Quote
        {
            GuildId = 9, Keyword = "K", AuthorName = "bob", AuthorId = BOB, Text = "b quote"
        });
        await ctx.GetTable<MusicPlaylist>().InsertAsync(() => new MusicPlaylist { Name = "mix", Author = "alice", AuthorId = ALICE });

        await ctx.Database.ExecuteSqlRawAsync($"""
            INSERT INTO CurrencyTransactions (Amount, Note, UserId, Type, Extra, OtherId)
            WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM n LIMIT {GIFTS_TO_BOB})
            SELECT 10, 'from alice', {BOB}, 'gift', 'alice', {ALICE} FROM n;
            INSERT INTO CurrencyTransactions (Amount, Note, UserId, Type, Extra, OtherId) VALUES
                (-10, 'from alice', {ALICE}, 'gift', 'alice', {BOB}),
                (-5, NULL, {ALICE}, 'blackjack', 'bet', NULL),
                (-10, 'hi', {BOB}, 'gift', 'bob', {CAROL}),
                (50, 'from alice', {BOB}, 'award', 'alice', {ALICE}),
                (-10, 'for alice', {BOB}, 'gift', 'bob', {ALICE}),
                (10, 'for alice', {ALICE}, 'gift', 'bob', {BOB}),
                (100, 'reason', {ALICE}, 'award', 'carol', {CAROL});
            """);

        var waifu = await WaifuTestHelper.CreateWaifu(ctx, ALICE, price: 1000, managerId: BOB);
        await ctx.GetTable<WaifuInfo>()
            .Where(x => x.Id == waifu.Id)
            .Set(x => x.Description, "about me")
            .Set(x => x.Quote, "my quote")
            .UpdateAsync();

        await ctx.GetTable<LineUpUser>().InsertAsync(() => new LineUpUser
        {
            GuildId = 9, ChannelId = 8, UserId = ALICE, Reason = "my reason", DateAdded = DateTime.UtcNow
        });

        var archiveId = await ctx.GetTable<ArchivedTodoListModel>().InsertWithInt32IdentityAsync(() => new ArchivedTodoListModel
        {
            UserId = ALICE, Name = "old"
        });
        await ctx.GetTable<TodoModel>().InsertAsync(() => new TodoModel
        {
            UserId = ALICE, Todo = "archived", DateAdded = DateTime.UtcNow, IsDone = true, ArchiveId = archiveId
        });
        foreach (var id in new[] { ALICE, BOB })
        {
            await ctx.GetTable<TodoModel>().InsertAsync(() => new TodoModel
            {
                UserId = id, Todo = "todo", DateAdded = DateTime.UtcNow, IsDone = false
            });
            await ctx.GetTable<Reminder>().InsertAsync(() => new Reminder
            {
                When = DateTime.UtcNow, ChannelId = 8, GuildId = 9, UserId = id, Message = "remind", IsPrivate = true,
                Type = ReminderType.User
            });
            await ctx.GetTable<ScheduledCommand>().InsertAsync(() => new ScheduledCommand
            {
                UserId = id, ChannelId = 8, GuildId = 9, MessageId = 7, Text = ".give 1 @x", When = DateTime.UtcNow
            });
        }

        await ctx.GetTable<Warning>().InsertAsync(() => new Warning
        {
            GuildId = 9, UserId = ALICE, Reason = "spam", Forgiven = false, Moderator = "mod", Weight = 1
        });
        await ctx.GetTable<UserXpStats>().InsertAsync(() => new UserXpStats { UserId = ALICE, GuildId = 9, Xp = 70 });
        await ctx.GetTable<BankUser>().InsertAsync(() => new BankUser { UserId = ALICE, Balance = 900 });
    }
}
