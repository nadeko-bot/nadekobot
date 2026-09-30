using System;
using System.Linq;
using System.Threading.Tasks;
using NadekoBot.Modules.Utility.LineUp;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class LineUpServiceTests
{
    private const ulong GUILD = 1;
    private const ulong CHANNEL = 2;

    private TestDbService _db = null!;
    private LineUpService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new LineUpService(_db);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task Join_KeepsOrder_RejectsDuplicates_AndPagesInOrder()
    {
        Assert.That(await _svc.TryJoinLineupAsync(GUILD, CHANNEL, 30, "first"), Is.EqualTo((true, 1)));
        Assert.That(await _svc.TryJoinLineupAsync(GUILD, CHANNEL, 10, null), Is.EqualTo((true, 2)));
        Assert.That(await _svc.TryJoinLineupAsync(GUILD, CHANNEL, 30, "again"), Is.EqualTo((false, 0)));
        Assert.That(await _svc.TryJoinLineupAsync(GUILD, CHANNEL + 1, 30, null), Is.EqualTo((true, 1)));

        Assert.That(await _svc.GetPositionAsync(GUILD, CHANNEL, 10), Is.EqualTo(2));
        Assert.That(await _svc.GetPositionAsync(GUILD, CHANNEL, 99), Is.Null);
        Assert.That(await _svc.GetLineupCountAsync(GUILD, CHANNEL), Is.EqualTo(2));

        var page = (await _svc.GetLineupPageAsync(GUILD, CHANNEL, 0, 10)).ToArray();
        Assert.That(page.Select(x => x.UserId), Is.EqualTo(new ulong[] { 30, 10 }));
        Assert.That(page[0].Reason, Is.EqualTo("first"));
        Assert.That(page[0].DateAdded.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task Next_GivesEachMemberOnce_EvenWhenCalledConcurrently()
    {
        for (ulong u = 1; u <= 5; u++)
            await _svc.TryJoinLineupAsync(GUILD, CHANNEL, u, null);

        var first = await _svc.GetNextInLineupAsync(GUILD, CHANNEL);
        Assert.That(first?.UserId, Is.EqualTo(1));
        Assert.That(first!.DateAdded.Kind, Is.EqualTo(DateTimeKind.Utc));

        var rest = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => _svc.GetNextInLineupAsync(GUILD, CHANNEL)));

        var popped = rest.Where(x => x is not null).Select(x => x!.UserId).OrderBy(x => x).ToArray();
        Assert.That(popped, Is.EqualTo(new ulong[] { 2, 3, 4, 5 }));
        Assert.That(await _svc.GetLineupCountAsync(GUILD, CHANNEL), Is.Zero);
    }
}
