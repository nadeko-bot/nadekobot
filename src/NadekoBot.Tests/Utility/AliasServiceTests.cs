using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common;
using NadekoBot.Common.Attributes;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.AiAgent;
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
    public async Task AddAlias_IgnoresCaseAndStopsAtTheLimitButStillUpdates()
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
        Assert.That(aliases.ContainsKey("A0"), Is.True);

        await using var ctx = db.GetDbContext();
        Assert.That(await ctx.GetTable<CommandAlias>().CountAsyncLinqToDB(x => x.GuildId == 1),
            Is.EqualTo(AliasService.MAX_ALIASES));
    }

    [Test]
    public async Task Alias_GuardsSpendingCommandsAndAgentFindFollowsTheHandler()
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

        // The AI agent runs what the command handler would run: an alias with the prefix hides a command of that name.
        using var db = new TestDbService();
        var handler = Substitute.For<ICommandHandler>();
        handler.GetPrefix(Arg.Any<IGuild>()).Returns(".");
        var svc = new AliasService(db,
            Substitute.For<IMessageSenderService>(),
            new ShardData(null!, Substitute.For<IBotCreds>()),
            cmds,
            handler);
        await svc.AddAliasAsync(1, "pay", ".give %target%");
        await svc.AddAliasAsync(1, ".hug", ".give %target%");
        await svc.AddAliasAsync(1, "tip", ".give 5 %target%");

        var guild = Substitute.For<IGuild>();
        guild.Id.Returns(1UL);
        var runner = new AgentCommandRunner(handler, cmds, svc);

        Assert.That(svc.FindAlias(guild, "pay 10 <@2>"), Is.EqualTo(new AliasMatch("pay", ".give  10 <@2>")));
        Assert.That(svc.FindAlias(guild, "tip <@2>"), Is.Null);
        // The alias trigger is one of the names by which the member asks for the command.
        IReadOnlyList<string> none = [];
        AgentCommandMatch? Find(string text)
            => runner.Find(guild, ".", text) is { } m ? m with { Aliases = none } : null;

        Assert.That(runner.Find(guild, ".", "pay 10 <@2>")?.Aliases, Is.EqualTo(new[] { "pay", "give" }));
        Assert.That(Find("pay 10 <@2>"),
            Is.EqualTo(new AgentCommandMatch("pay", "10 <@2>", true, "pay 10 <@2>", "give", "10 <@2>", none)));
        Assert.That(Find("hug 10 <@2>"),
            Is.EqualTo(new AgentCommandMatch("hug", "10 <@2>", true, ".hug 10 <@2>", "give", "10 <@2>", none)));
        Assert.That(Find("betflip 10 heads"),
            Is.EqualTo(new AgentCommandMatch("betflip", "10 heads", true, ".betflip 10 heads", "betflip", "10 heads", none)));
        Assert.That(Find("tip <@2>"), Is.Null);
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
