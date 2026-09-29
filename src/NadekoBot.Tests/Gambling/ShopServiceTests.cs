using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class ShopServiceTests
{
    private const ulong GUILD_ID = 1;
    private const ulong OTHER_GUILD_ID = 2;

    private TestDbService _db = null!;
    private ShopService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _db = new();
        _svc = new(_db);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task AddEditRemove_UseTheListedPosition()
    {
        await SeedAsync(GUILD_ID, "a", 0);
        await SeedAsync(OTHER_GUILD_ID, "other", 1);
        await SeedAsync(GUILD_ID, "b", 5);

        var cmd = await _svc.AddShopCommandAsync(GUILD_ID, 5, 10, ".say hi");
        var role = await _svc.AddShopRoleAsync(GUILD_ID, 5, 20, 42, "vip");

        Assert.That(cmd.Type, Is.EqualTo(ShopEntryType.Command));
        Assert.That(role.RoleId, Is.EqualTo(42UL));
        Assert.That(await NamesAsync(GUILD_ID), Is.EqualTo(new[] { "a", "b", ".say hi", "-" }));

        Assert.That(await _svc.ChangeEntryPriceAsync(GUILD_ID, 1, 500), Is.True);
        Assert.That(await _svc.ChangeEntryPriceAsync(GUILD_ID, 4, 500), Is.False);

        var removed = await _svc.RemoveEntryAsync(GUILD_ID, 0);
        Assert.That(removed?.Name, Is.EqualTo("a"));
        Assert.That(await _svc.RemoveEntryAsync(GUILD_ID, 3), Is.Null);

        await using var uow = _db.GetDbContext();
        var left = await ShopService.Ordered(uow.GetTable<ShopEntry>(), GUILD_ID)
                                    .Select(x => new { x.Name, x.Index, x.Price })
                                    .ToArrayAsyncLinqToDB();

        Assert.That(left.Select(x => x.Name), Is.EqualTo(new[] { "b", ".say hi", "-" }));
        Assert.That(left.Select(x => x.Index), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(left[0].Price, Is.EqualTo(500));
    }

    [Test]
    public async Task SwapAndMove_ChangeTheListedOrder()
    {
        await SeedAsync(GUILD_ID, "a", 0);
        await SeedAsync(GUILD_ID, "b", 1);
        await SeedAsync(GUILD_ID, "c", 2);

        Assert.That(await _svc.SwapEntriesAsync(GUILD_ID, 0, 2), Is.True);
        Assert.That(await NamesAsync(GUILD_ID), Is.EqualTo(new[] { "c", "b", "a" }));

        Assert.That(await _svc.MoveEntryAsync(GUILD_ID, 0, 2), Is.True);
        Assert.That(await NamesAsync(GUILD_ID), Is.EqualTo(new[] { "b", "a", "c" }));

        Assert.That(await _svc.ChangeEntryNameAsync(GUILD_ID, 0, "first"), Is.True);
        Assert.That(await NamesAsync(GUILD_ID), Is.EqualTo(new[] { "first", "a", "c" }));
    }

    [Test]
    public async Task RemoveListEntry_RemovesItsItemsAndShowsTheirCount()
    {
        var list = await _svc.AddShopListAsync(GUILD_ID, 5, 10, "keys");
        await SeedAsync(GUILD_ID, "other", 5);

        await using (var uow = _db.GetDbContext())
        {
            var entry = await uow.Set<ShopEntry>().FirstAsyncEF(x => x.Id == list.Id);
            entry.Items.Add(new() { Text = "k1" });
            entry.Items.Add(new() { Text = "k2" });
            await uow.SaveChangesAsync();
        }

        var removed = await _svc.RemoveEntryAsync(GUILD_ID, 0);

        Assert.That(removed?.Name, Is.EqualTo("keys"));
        Assert.That(removed?.Items.Count, Is.EqualTo(2));
        Assert.That(await NamesAsync(GUILD_ID), Is.EqualTo(new[] { "other" }));

        await using var ctx = _db.GetDbContext();
        Assert.That(await ctx.GetTable<ShopEntryItem>().CountAsyncLinqToDB(), Is.EqualTo(0));
    }

    private async Task<string[]> NamesAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        return await ShopService.Ordered(uow.GetTable<ShopEntry>(), guildId)
                                .Select(x => x.Name)
                                .ToArrayAsyncLinqToDB();
    }

    private async Task SeedAsync(ulong guildId, string name, int index)
    {
        await using var uow = _db.GetDbContext();
        await uow.GetTable<ShopEntry>()
                 .InsertAsync(() => new()
                 {
                     GuildId = guildId,
                     Name = name,
                     Index = index,
                     Price = 100,
                     AuthorId = 1,
                     RoleId = 0,
                     Type = ShopEntryType.List,
                 });
    }
}
