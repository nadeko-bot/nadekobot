#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NadekoBot.Common;
using NadekoBot.Modules.Utility.AiAgent;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class AiAgentActivityTests
{
    private const ulong GUILD_ID = 1;

    [Test]
    public async Task Session_ReportsNotesAndToolSteps()
    {
        var handler = new ScriptedHandler(
            """{"choices":[{"message":{"role":"assistant","content":"Checking the queue\nfor you.","tool_calls":[{"id":"c1","type":"function","function":{"name":"ok","arguments":"{\"query\":\"q\"}"}},{"id":"c2","type":"function","function":{"name":"bad","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
            """{"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"c3","type":"function","function":{"name":"missing","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
            """{"choices":[{"message":{"role":"assistant","content":"All done"},"finish_reason":"stop"}]}""");

        var progress = new RecordingProgress();
        var result = await CreateSession(handler).RunAsync(
            new AiAgentPrompt("SYSTEM", "CONTEXT", null, "TURN", "REQUEST"),
            CreateContext(),
            [new FixedTool("ok", "fine"), new FixedTool("bad", """{"error":"forbidden","message":"no"}""")],
            [],
            new AiAgentConfig { MaxToolCalls = 5 },
            progress);

        Assert.That(result.IsT0, Is.True);
        Assert.That(result.AsT0.Response, Is.EqualTo("All done"));
        Assert.That(progress.Notes, Is.EqualTo(new[] { "Checking the queue\nfor you." }));
        Assert.That(progress.Calls, Is.EqualTo(new[] { ("ok", false), ("bad", true), ("missing", true) }));
        Assert.That(progress.Args[0], Is.EqualTo("q"));
    }

    [Test]
    public void Formatter_DescribesStepsInPlainWords()
    {
        var strings = CreateStrings();

        string? Tool(string name, string args, bool failed = false)
        {
            using var doc = JsonDocument.Parse(args);
            return AgentActivityFormatter.FormatToolCall(name, doc.RootElement, failed, ".", strings, GUILD_ID);
        }

        var run = Tool("run_command", """{"command":"mute <@123> 10m spam"}""");
        Assert.That(run, Does.Contain("`.mute`").And.Not.Contain("123").And.Not.Contain("spam"));
        Assert.That(Tool("run_command", """{"command":".ban <@5>"}"""), Does.Contain("`.ban`").And.Not.Contain(".."));
        Assert.That(
            Tool("run_command_chain", """{"commands":["warn <@1> a","mute <@1>"]}"""),
            Does.Contain("`.warn`, `.mute`"));

        Assert.That(Tool("describe_data_tool", """{"name":"get_user_xp"}"""), Is.Null);
        Assert.That(Tool("ask_user", """{"question":"which one?"}"""), Is.Null);
        Assert.That(Tool("close_session", "{}"), Is.Null);

        Assert.That(Tool("invoke_data_tool", """{"name":"get_user_xp"}"""), Does.Contain("user xp"));
        Assert.That(Tool("send_message", """{"channel":"789","message":"hi"}"""), Does.Contain("<#789>"));
        Assert.That(Tool("some_new_tool", "{}"), Does.Contain("agent_step_other").And.Contain("some new tool"));
        Assert.That(
            Tool("web_search", """{"query":"weather"}""", failed: true),
            Does.StartWith("🌐 agent_step_failed(agent_step_web_search(weather))"));

        var note = AgentActivityFormatter.FormatNote("First line\n\nsecond   line " + new string('x', 300));
        Assert.That(note, Does.StartWith("💭 First line second line").And.EndWith("...").And.Not.Contain("\n"));
        Assert.That(note.Length, Is.LessThan(220));
    }

    [Test]
    public void Formatter_Render_KeepsNewestStepsWithinLimit()
    {
        var strings = CreateStrings();
        var lines = Enumerable.Range(0, 64).Select(static i => $"line{i:00} " + new string('x', 140)).ToList();

        var text = AgentActivityFormatter.Render(lines, 10, 2000, "**Working**", true, strings, GUILD_ID);

        Assert.That(text.Length, Is.LessThanOrEqualTo(2000));
        Assert.That(text, Does.StartWith("**Working**\n-# *agent_activity_earlier("));
        Assert.That(text, Does.Contain("line63").And.Not.Contain("line00"));

        var shown = text.Split('\n').Count(static l => l.StartsWith("-# line", StringComparison.Ordinal));
        Assert.That(text, Does.Contain($"agent_activity_earlier({10 + 64 - shown})"));

        var all = AgentActivityFormatter.Render(lines.Take(3).ToList(), 0, 4096, null, false, strings, GUILD_ID);
        Assert.That(all.Split('\n'), Has.Length.EqualTo(3));
        Assert.That(all, Does.Not.Contain("agent_activity_earlier"));
    }

    private static IBotStrings CreateStrings()
    {
        var strings = Substitute.For<IBotStrings>();
        strings.GetText(Arg.Any<string>(), Arg.Any<ulong?>(), Arg.Any<object[]>())
            .Returns(static ci =>
            {
                var key = ci.ArgAt<string>(0);
                var data = ci.ArgAt<object[]>(2);
                return data.Length == 0 ? key : $"{key}({string.Join("|", data)})";
            });
        return strings;
    }

    private static AiAgentSession CreateSession(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var creds = Substitute.For<IBotCreds>();
        creds.AiApiKey.Returns("key");
        var credsProvider = Substitute.For<IBotCredsProvider>();
        credsProvider.GetCreds().Returns(creds);

        return new AiAgentSession(factory, credsProvider, new NadekoBot.Common.BotPolicy(false));
    }

    private static AiToolContext CreateContext()
        => new()
        {
            Guild = Substitute.For<Discord.IGuild>(),
            SourceChannel = Substitute.For<Discord.ITextChannel>(),
            User = Substitute.For<Discord.IGuildUser>(),
            TriggerMessage = Substitute.For<Discord.IUserMessage>(),
            CancellationToken = CancellationToken.None
        };

    private sealed class RecordingProgress : IAgentProgress
    {
        public List<string> Notes { get; } = [];
        public List<(string, bool)> Calls { get; } = [];
        public List<string?> Args { get; } = [];

        public void OnNote(string text)
            => Notes.Add(text);

        public void OnToolCall(string name, JsonElement args, bool failed)
        {
            Calls.Add((name, failed));
            Args.Add(args.ValueKind == JsonValueKind.Object && args.TryGetProperty("query", out var q)
                ? q.GetString()
                : null);
        }
    }

    private sealed class FixedTool(string name, string result) : IAiTool
    {
        public string Name => name;
        public string Description => "Fixed result.";
        public JsonElement ParameterSchema { get; } =
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

        public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
            => Task.FromResult(result);
    }

    private sealed class ScriptedHandler(params string[] responses) : HttpMessageHandler
    {
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[_next++], Encoding.UTF8, "application/json")
            });
    }
}
