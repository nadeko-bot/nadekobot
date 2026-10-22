#nullable enable
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NadekoBot.Common;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Utility.AiAgent;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class NoAiAgentTests
{
    [Test]
    public async Task NoAiAgent_RefusesEveryCommandWhichTheBotRunsForSomeone()
    {
        var owner = Substitute.For<IUser>();
        var typed = Substitute.For<IUserMessage>();
        typed.Author.Returns(owner);

        ICommandContext Context(IUserMessage msg)
        {
            var ctx = Substitute.For<ICommandContext>();
            ctx.User.Returns(owner);
            ctx.Message.Returns(msg);
            return ctx;
        }

        var attr = new NoAiAgentAttribute();
        Assert.That((await attr.CheckPermissionsAsync(Context(typed), null!, null!)).IsSuccess, Is.True);

        foreach (var blocks in new[] { true, false })
        {
            var doAs = new DoAsUserMessage(typed, owner, ".sqlselect SELECT 1") { BlocksOwnerCommands = blocks };
            Assert.That((await attr.CheckPermissionsAsync(Context(doAs), null!, null!)).IsSuccess, Is.False);
        }
    }

    [Test]
    public void CommandSearch_LeavesOutNoAiAgentCommands()
    {
        var json = $$"""
            {
              "Owner": [
                { "Aliases": [".sqlselect"], "Description": "x", "Usage": [], "Submodule": "SqlCommands",
                  "Requirements": ["Bot Owner Only", "{{NoAiAgentAttribute.REQUIREMENT}}"] },
                { "Aliases": [".setstatus"], "Description": "y", "Usage": [], "Submodule": "Owner",
                  "Requirements": ["Bot Owner Only"] }
              ]
            }
            """;

        var entries = CommandSearchService.ParseCommandList(Encoding.UTF8.GetBytes(json));

        Assert.That(entries, Has.Length.EqualTo(1));
        Assert.That(entries[0].Aliases, Is.EqualTo(new[] { "setstatus" }));
    }

    [Test]
    public async Task Requirements_SeeNoAiAgentOnEveryParentGroup()
    {
        var cmds = new CommandService();
        await cmds.AddModuleAsync<TopModule>(null);

        var inner = cmds.Commands.Single(x => x.Name == "inner");
        var open = cmds.Commands.Single(x => x.Name == "open");

        Assert.That(CommandsUtilityService.GetCommandRequirements(inner), Does.Contain(NoAiAgentAttribute.REQUIREMENT));
        Assert.That(CommandsUtilityService.GetCommandRequirements(open), Does.Not.Contain(NoAiAgentAttribute.REQUIREMENT));
    }

    public sealed class TopModule : ModuleBase<ICommandContext>
    {
        [Command("open")]
        public Task Open()
            => Task.CompletedTask;

        [Group("mid")]
        [NoAiAgent]
        public sealed class MidModule : ModuleBase<ICommandContext>
        {
            [Group("low")]
            public sealed class LowModule : ModuleBase<ICommandContext>
            {
                [Command("inner")]
                public Task Inner()
                    => Task.CompletedTask;
            }
        }
    }
}
