#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Modules.Utility.AiAgent.Prompts;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class SystemPromptBuilderTests
{
    private sealed class FakeTool(string name, string? guidance) : IAiTool
    {
        public string Name => name;
        public string Description => "fake";
        public JsonElement ParameterSchema { get; } = JsonDocument.Parse("{}").RootElement;
        public string? SystemGuidance => guidance;

        public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
            => Task.FromResult(string.Empty);
    }

    [Test]
    public void Compose_DefaultPromptsHaveNoPerTurnData_AndPerTurnTokensAreReplaced()
    {
        var snapshot = new PromptSnapshot(DefaultPrompts.Soul, DefaultPrompts.Operator);

        Assert.That(SystemPromptBuilder.HasTurnTokens(snapshot), Is.False);

        var prompt = SystemPromptBuilder.Compose(snapshot, "Nadeko", 42, ["guide"], null);

        Assert.That(prompt, Does.StartWith("You are Nadeko,"));
        Assert.That(prompt, Does.Contain("guide"));
        Assert.That(prompt, Does.Not.Contain("Current time").And.Not.Contain("CONTEXT:").And.Not.Contain("{"));

        var withTokens = new PromptSnapshot("Hi {userName} in #{channelName} on {guildName}, I am {botName}", "");

        Assert.That(SystemPromptBuilder.HasTurnTokens(withTokens), Is.True);
        Assert.That(SystemPromptBuilder.Compose(withTokens, "Nadeko", 42, [], new TurnTokens("Guild", "general", "Bob")),
            Does.StartWith("Hi Bob in #general on Guild, I am Nadeko"));
    }

    [Test]
    public void CollectToolGuidance_SkipsBlankDeduplicatesAndSorts()
    {
        Assert.That(SystemPromptBuilder.CollectToolGuidance(new List<IAiTool>()), Is.Empty);

        var tools = new List<IAiTool>
        {
            new FakeTool("z-tool", "zzzz guidance"),
            new FakeTool("none", null),
            new FakeTool("blank", "   \n\t  "),
            new FakeTool("a-tool", "aaaa guidance"),
            new FakeTool("m-tool", "mmmm guidance"),
            new FakeTool("m2-tool", "mmmm guidance")
        };

        Assert.That(SystemPromptBuilder.CollectToolGuidance(tools), Is.EqualTo(new[]
        {
            "aaaa guidance",
            "mmmm guidance",
            "zzzz guidance"
        }));
    }
}
