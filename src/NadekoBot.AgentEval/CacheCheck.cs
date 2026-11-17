using System.Collections.Immutable;
using System.Text.Json;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility.AiAgent.Prompts;

namespace NadekoBot.AgentEval;

public sealed record CachePart(string Name, int Chars, bool Same);

public sealed record CacheReport(IReadOnlyList<CachePart> Parts, int SharedChars, int TotalChars, string FirstDifference, string? Failure);

// The start which the second request of a member shares with the first is what a provider can read from its prompt cache.
public static class CacheCheck
{
    private const ulong CHANNEL_ID = 200;
    private const ulong MEMBER_ID = 300;
    private const int HISTORY = 20;

    private static readonly string[] _names = ["system", "context", "history", "turn", "request"];

    public static async Task<CacheReport> RunAsync(EvalServices services)
    {
        using var server = new FakeServer(FakeCommands.All());
        using var model = new ScriptedModel(static _ => ModelReply.Say("ok"), "ok");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        var session = new AiAgentSession(new FakeHttpFactory(model), services.Creds, new BotPolicy(false));
        var tools = services.CreateTools(server);
        var schemas = new AiToolRegistry(tools).GetToolSchemas();
        var system = SystemPromptBuilder.Compose(
            new PromptSnapshot(DefaultPrompts.Soul, DefaultPrompts.Operator),
            "Nadeko",
            1,
            SystemPromptBuilder.CollectToolGuidance(tools),
            null);

        var (bot, channels) = CreateServerInternal();
        ImmutableArray<AiAgentGuildSkill> skills =
        [
            Skill(1, CHANNEL_ID, "support", "Answer in short steps."),
            Skill(3, null, "tone", "Reply in a calm, formal tone."),
            Skill(2, null, "prices", "Do not tell prices. Tell members to read #pricing.")
        ];

        var buffer = new ChannelMessageBuffer(HISTORY);
        for (ulong i = 0; i < HISTORY; i++)
            buffer.Push(new(1000 + i, 500 + i % 4, "member" + i % 4, $"message number {i} about the event of this week", Time(i)));

        buffer.Push(new(1020, MEMBER_ID, "member", "show my balance", Time(20)));
        var start = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        await RunRequestInternalAsync("show my balance", 1020, start);

        buffer.Push(new(1021, 501, "member1", "nice", Time(21)));
        buffer.Push(new(1022, 502, "member2", "same here", Time(22)));
        buffer.Push(new(1023, MEMBER_ID, "member", "and the leaderboard", Time(23)));
        await RunRequestInternalAsync("and the leaderboard", 1023, start.AddSeconds(30));

        var bodies = model.Bodies;
        if (bodies.Count != 2)
            return new([], 0, 0, "", $"{bodies.Count} model requests instead of 2");

        return CompareInternal(bodies[0], bodies[1], "and the leaderboard");

        async Task RunRequestInternalAsync(string request, ulong triggerId, DateTimeOffset now)
        {
            model.StartRequest();
            var ctx = FakeDiscord.CreateContext(request, null, new AgentConversation(Environment.TickCount64), cts.Token);
            ctx.Guild.GetCurrentUserAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(Task.FromResult(bot));
            ctx.Guild.GetTextChannelsAsync(Arg.Any<CacheMode>(), Arg.Any<RequestOptions>()).Returns(Task.FromResult(channels));

            var prompt = await AiAgentService.CreatePromptAsync(
                system,
                ctx,
                skills,
                new ChannelHistoryFeed(buffer, CHANNEL_ID, "general", triggerId, MEMBER_ID),
                now,
                AgentConversation.PromptFor(request, null),
                AgentHitFormatter.Candidates(services.Search, request));

            await session.RunAsync(prompt, ctx, tools, schemas, EvalServices.CreateConfig(), null, cts.Token);
        }
    }

    private static CacheReport CompareInternal(string firstBody, string secondBody, string request)
    {
        var first = PartsInternal(firstBody);
        var second = PartsInternal(secondBody);
        if (first.Count != _names.Length + 1 || second.Count != first.Count)
            return new([], 0, 0, "", $"the requests have {first.Count - 1} and {second.Count - 1} messages instead of {_names.Length}");

        var parts = new List<CachePart>(first.Count);
        var shared = 0;
        var total = 0;
        var difference = "";
        for (var i = 0; i < first.Count; i++)
        {
            var name = i == 0 ? "tools" : _names[i - 1];
            var same = string.Equals(first[i].Raw, second[i].Raw, StringComparison.Ordinal);
            parts.Add(new(name, second[i].Raw.Length, same));
            total += second[i].Raw.Length;
            if (difference.Length > 0)
                continue;

            if (same)
                shared += second[i].Raw.Length;
            else
                difference = name;
        }

        string? failure = null;
        if (first[0].Raw.Length == 0)
            failure = "the requests have no tools";
        else if (!parts[0].Same || !parts[1].Same || !parts[2].Same)
            failure = "the tools, the system prompt or the context changed between two requests of one server";
        else if (ClockBeforeHistoryInternal(second))
            failure = "the clock is in front of the channel history";
        else if (!StartsInternal(second[2], "CONTEXT:")
                 || !StartsInternal(second[3], "<channel_history")
                 || !StartsInternal(second[4], "CURRENT TURN:")
                 || !StartsInternal(second[5], request))
            failure = "the messages are not in the order context, history, turn, request";
        else if (SkillsAfterChannelsInternal(second[2].Text))
            failure = "the skills are not in front of the channel list of the context";

        return new(parts, shared, total, difference.Length > 0 ? difference : "none", failure);
    }

    private static bool StartsInternal((string Raw, string Text) part, string start)
        => part.Text.StartsWith(start, StringComparison.Ordinal);

    private static bool SkillsAfterChannelsInternal(string context)
    {
        var skills = context.IndexOf("[SERVER INSTRUCTIONS", StringComparison.Ordinal);
        return skills < 0 || skills > context.IndexOf("- Available channels:", StringComparison.Ordinal);
    }

    private static bool ClockBeforeHistoryInternal(List<(string Raw, string Text)> parts)
    {
        foreach (var (_, text) in parts)
        {
            if (text.StartsWith("<channel_history", StringComparison.Ordinal))
                return false;

            if (text.Contains("- Current time: ", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // The tools, then each message: the bytes as the request sends them, and the text which the model reads.
    private static List<(string Raw, string Text)> PartsInternal(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var parts = new List<(string Raw, string Text)> { (root.TryGetProperty("tools", out var tools) ? tools.GetRawText() : "", "") };
        foreach (var m in root.GetProperty("messages").EnumerateArray())
            parts.Add((m.GetRawText(), m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : ""));

        return parts;
    }

    private static (IGuildUser Bot, IReadOnlyCollection<ITextChannel> Channels) CreateServerInternal()
    {
        var bot = Substitute.For<IGuildUser>();
        bot.Id.Returns(1UL);
        bot.DisplayName.Returns("Nadeko");
        bot.GetPermissions(Arg.Any<IGuildChannel>()).Returns(ChannelPermissions.Text);

        string[] names = ["rules", "announcements", "general", "memes", "music", "bot-commands", "support", "pricing"];
        var channels = new List<ITextChannel>(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            var channel = Substitute.For<ITextChannel>();
            channel.Id.Returns(names[i] == "general" ? CHANNEL_ID : 600UL + (ulong)i);
            channel.Name.Returns(names[i]);
            channel.Position.Returns(i);
            channels.Add(channel);
        }

        return (bot, channels);
    }

    private static AiAgentGuildSkill Skill(int id, ulong? channelId, string name, string instruction)
        => new() { Id = id, GuildId = 100, ChannelId = channelId, Name = name, Instruction = instruction, IsEnabled = true };

    private static DateTimeOffset Time(ulong minute)
        => new DateTimeOffset(2026, 11, 17, 11, 0, 0, TimeSpan.Zero).AddMinutes(minute);
}
