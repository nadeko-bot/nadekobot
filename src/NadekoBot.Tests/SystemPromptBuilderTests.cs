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
    public void CollectToolGuidance_NoTools_ReturnsEmpty()
    {
        var result = SystemPromptBuilder.CollectToolGuidance(new List<IAiTool>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CollectToolGuidance_NullGuidance_Skipped()
    {
        var tools = new List<IAiTool>
        {
            new FakeTool("a", null),
            new FakeTool("b", "use tool b wisely")
        };

        var result = SystemPromptBuilder.CollectToolGuidance(tools);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("use tool b wisely"));
    }

    [Test]
    public void CollectToolGuidance_WhitespaceGuidance_Skipped()
    {
        var tools = new List<IAiTool>
        {
            new FakeTool("a", "   \n\t  "),
            new FakeTool("b", "real guidance")
        };

        var result = SystemPromptBuilder.CollectToolGuidance(tools);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("real guidance"));
    }

    [Test]
    public void CollectToolGuidance_DuplicateGuidance_Deduplicated()
    {
        var tools = new List<IAiTool>
        {
            new FakeTool("a", "same guidance"),
            new FakeTool("b", "same guidance"),
            new FakeTool("c", "different guidance")
        };

        var result = SystemPromptBuilder.CollectToolGuidance(tools);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void CollectToolGuidance_SortedAlphabetically()
    {
        var tools = new List<IAiTool>
        {
            new FakeTool("z-tool", "zzzz guidance"),
            new FakeTool("a-tool", "aaaa guidance"),
            new FakeTool("m-tool", "mmmm guidance")
        };

        var result = SystemPromptBuilder.CollectToolGuidance(tools);

        Assert.That(result, Is.EqualTo(new[]
        {
            "aaaa guidance",
            "mmmm guidance",
            "zzzz guidance"
        }));
    }
}
