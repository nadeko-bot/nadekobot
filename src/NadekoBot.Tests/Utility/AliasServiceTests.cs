using System;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Tests.Waifu;
using NonBlocking;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class AliasServiceTests
{
    [Test]
    public void FindMapping_PrefersExactThenLongestTrigger()
    {
        var maps = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        maps["hi"] = ".say hello";
        maps["hi all"] = ".say hello everyone";
        maps["hug"] = ".hug %target% tightly";

        Assert.That(AliasService.FindMapping(maps, "HI"), Is.EqualTo(".say hello"));
        Assert.That(AliasService.FindMapping(maps, "hi all today"), Is.EqualTo(".say hello everyone  today"));
        Assert.That(AliasService.FindMapping(maps, "hi there"), Is.EqualTo(".say hello  there"));
        Assert.That(AliasService.FindMapping(maps, "hug @bob"), Is.EqualTo(".hug  @bob tightly"));
        Assert.That(AliasService.FindMapping(maps, "hiall"), Is.Null);
    }

    [Test]
    public async Task AddedAlias_IsCaseInsensitive()
    {
        using var db = new TestDbService();
        var svc = new AliasService(db,
            Substitute.For<IMessageSenderService>(),
            new ShardData(null!, Substitute.For<IBotCreds>()));

        await svc.AddAliasAsync(1, "hi", ".say hello");

        var aliases = await svc.GetAliasesAsync(1);
        Assert.That(aliases!.ContainsKey("HI"), Is.True);
    }

    [Test]
    public async Task AddAlias_StopsAtTheLimitButStillUpdates()
    {
        using var db = new TestDbService();
        var svc = new AliasService(db,
            Substitute.For<IMessageSenderService>(),
            new ShardData(null!, Substitute.For<IBotCreds>()));

        for (var i = 0; i < AliasService.MAX_ALIASES; i++)
            Assert.That(await svc.AddAliasAsync(1, $"a{i}", ".ping"), Is.EqualTo(AliasAddResult.Added));

        Assert.That(await svc.AddAliasAsync(1, "extra", ".ping"), Is.EqualTo(AliasAddResult.LimitReached));
        Assert.That(await svc.AddAliasAsync(1, "a0", ".stats"), Is.EqualTo(AliasAddResult.Updated));
        Assert.That(await svc.AddAliasAsync(2, "extra", ".ping"), Is.EqualTo(AliasAddResult.Added));

        var aliases = await svc.GetAliasesAsync(1);
        Assert.That(aliases!.Count, Is.EqualTo(AliasService.MAX_ALIASES));
        Assert.That(aliases.ContainsKey("extra"), Is.False);
        Assert.That(aliases["a0"], Is.EqualTo(".stats"));

        await using var ctx = db.GetDbContext();
        Assert.That(await ctx.GetTable<CommandAlias>().CountAsyncLinqToDB(x => x.GuildId == 1),
            Is.EqualTo(AliasService.MAX_ALIASES));
    }
}
