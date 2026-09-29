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
using NadekoBot.Modules.Utility.AiAgent;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class AiAgentPromptCacheTests
{
    private const ulong CHANNEL_ID = 500;
    private const ulong TRIGGER_ID = 12;

    private static MessageSnapshot Msg(ulong id, string text)
        => new(id, 100, "Alice", text, DateTimeOffset.UnixEpoch.AddSeconds(id));

    [Test]
    public void HistoryFeed_SnapshotThenOnlyNewMessages()
    {
        var buffer = new ChannelMessageBuffer(10);
        buffer.Push(Msg(10, "old-a"));
        buffer.Push(Msg(11, "old-b"));
        buffer.Push(Msg(TRIGGER_ID, "trigger"));

        var feed = new ChannelHistoryFeed(buffer, CHANNEL_ID, "general", TRIGGER_ID);

        var snapshot = feed.GetSnapshot();
        Assert.That(snapshot, Does.Contain("old-a").And.Contain("old-b").And.Not.Contain("trigger"));
        Assert.That(feed.GetUpdate(), Is.Null);

        buffer.Push(Msg(13, "new-a"));
        buffer.Push(Msg(14, "new-b"));

        var update = feed.GetUpdate();
        Assert.That(update, Does.Contain("new-a").And.Contain("new-b"));
        Assert.That(update, Does.Not.Contain("old-a").And.Not.Contain("trigger"));
        Assert.That(feed.GetUpdate(), Is.Null);
    }

    [Test]
    public void HistoryFeed_EmptyBuffer_UpdateStartsAfterTrigger()
    {
        var buffer = new ChannelMessageBuffer(10);
        var feed = new ChannelHistoryFeed(buffer, CHANNEL_ID, "general", TRIGGER_ID);

        Assert.That(feed.GetSnapshot(), Is.Null);

        buffer.Push(Msg(TRIGGER_ID, "trigger"));
        buffer.Push(Msg(13, "reply"));

        var update = feed.GetUpdate();
        Assert.That(update, Does.Contain("reply").And.Not.Contain("trigger"));
    }

    [Test]
    public async Task Session_EveryRequestExtendsThePreviousOne()
    {
        var buffer = new ChannelMessageBuffer(3);
        buffer.Push(Msg(10, "old-a"));
        buffer.Push(Msg(11, "old-b"));
        buffer.Push(Msg(TRIGGER_ID, "trigger"));

        // The tool posts a message, which also evicts the oldest entry of the full ring buffer.
        var tool = new PostingTool(buffer, Msg(13, "bot-posted"));

        var handler = new ScriptedHandler(
            """{"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"post","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""",
            """{"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}""");

        var session = CreateSession(handler);
        var prompt = new AiAgentPrompt(
            "SYSTEM",
            "CONTEXT",
            new ChannelHistoryFeed(buffer, CHANNEL_ID, "general", TRIGGER_ID),
            "TURN");

        var result = await session.RunAsync(
            prompt,
            CreateContext(),
            [tool],
            [],
            new AiAgentConfig { MaxToolCalls = 5 });

        Assert.That(result.IsT0, Is.True);
        Assert.That(result.AsT0.Response, Is.EqualTo("done"));
        Assert.That(handler.Bodies, Has.Count.EqualTo(2));

        var first = Messages(handler.Bodies[0]);
        var second = Messages(handler.Bodies[1]);

        Assert.That(first[0], Does.Contain("SYSTEM"));
        Assert.That(first[1], Does.Contain("CONTEXT").And.Contain("old-a"));
        Assert.That(first[2], Does.Contain("TURN"));

        Assert.That(second.Take(first.Count), Is.EqualTo(first));
        Assert.That(second[^1], Does.Contain("\"tool\"").And.Contain("bot-posted"));
    }

    private static List<string> Messages(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("messages").EnumerateArray().Select(static m => m.GetRawText()).ToList();
    }

    private static AiAgentSession CreateSession(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var creds = Substitute.For<IBotCreds>();
        creds.AiApiKey.Returns("key");
        var credsProvider = Substitute.For<IBotCredsProvider>();
        credsProvider.GetCreds().Returns(creds);

        return new AiAgentSession(factory, credsProvider);
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

    private sealed class PostingTool(ChannelMessageBuffer buffer, MessageSnapshot posted) : IAiTool
    {
        public string Name => "post";
        public string Description => "Posts a message.";
        public JsonElement ParameterSchema { get; } =
            JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

        public Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
        {
            buffer.Push(posted);
            return Task.FromResult("posted");
        }
    }

    private sealed class ScriptedHandler(params string[] responses) : HttpMessageHandler
    {
        private int _next;
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[_next++], Encoding.UTF8, "application/json")
            };
        }
    }
}
