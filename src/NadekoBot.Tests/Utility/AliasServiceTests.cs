using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Common.Attributes;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.Services;
using NadekoBot.Services;
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
        var svc = CreateService(db);

        await svc.AddAliasAsync(1, "hi", ".say hello");

        var aliases = await svc.GetAliasesAsync(1);
        Assert.That(aliases!.ContainsKey("HI"), Is.True);
    }

    [Test]
    public async Task AddAlias_StopsAtTheLimitButStillUpdates()
    {
        using var db = new TestDbService();
        var svc = CreateService(db);

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

    [Test]
    public async Task SpendingCommand_TakesOnlyTargetArgsFromAnAlias()
    {
        var cmds = new CommandService();
        await cmds.AddModuleAsync<SpendingTestModule>(null);

        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", ".bf 10 heads"), Is.True);
        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", " .BETFLIP 10 heads"), Is.True);
        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", ".give 1000 <@123>"), Is.True);
        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", ".give %target% 5"), Is.True);

        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", ".bf"), Is.False);
        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", ".bf %target%"), Is.False);
        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, ".", ".hug 10 heads"), Is.False);
        Assert.That(AliasService.GivesArgsToSpendingCommand(cmds, "!", ".bf 10 heads"), Is.False);
    }

    private static AliasService CreateService(TestDbService db)
        => new(db,
            Substitute.For<IMessageSenderService>(),
            new ShardData(null!, Substitute.For<IBotCreds>()),
            new CommandService(),
            Substitute.For<ICommandHandler>());
}

public sealed class SpendingTestModule : ModuleBase<ICommandContext>
{
    [Command("betflip")]
    [Alias("bf")]
    [SpendsCurrency]
    public Task Betflip(long amount = 0, string guess = "")
        => Task.CompletedTask;

    [Command("give")]
    [SpendsCurrency]
    public Task Give(long amount, IUser user)
        => Task.CompletedTask;

    [Command("hug")]
    public Task Hug([Remainder] string target = "")
        => Task.CompletedTask;
}
