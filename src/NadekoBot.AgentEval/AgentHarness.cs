using System.Diagnostics;
using Discord.WebSocket;
using NadekoBot.Modules.Utility.AiAgent.Adapters;

namespace NadekoBot.AgentEval;

public sealed class FakeCommandRunner(FakeServer server) : IAgentCommandRunner
{
    public AgentCommandMatch? Find(IGuild guild, string prefix, string text)
    {
        // Like the real runner: a text with the prefix names no command, so run_command strips the prefix first.
        if (text.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var (name, args) = FakeServer.Split(text);
        return server.TryFind(name, out var cmd)
            ? new(cmd.Name, args, cmd.SpendsCurrency, prefix + text, cmd.Name, args, cmd.Aliases)
            : null;
    }

    public async Task<CommandRunResult> RunAsync(IGuild guild, ITextChannel channel, DoAsUserMessage message)
    {
        var (name, args) = FakeServer.Split(message.Content);
        if (!server.TryFind(name, out var cmd))
        {
            server.CountDispatch("?" + name, executed: false);
            return new(CommandRunStatus.NotFound);
        }

        var run = cmd.Run(args);
        server.CountDispatch(cmd.Name, executed: run.Kind is RunKind.Ok or RunKind.Error);
        switch (run.Kind)
        {
            case RunKind.BadArgs:
                return new(CommandRunStatus.BadArguments, run.Reason);
            case RunKind.Refused:
                return new(CommandRunStatus.Refused, run.Reason);
        }

        try
        {
            await Task.Delay(run.OutputDelayMs, server.Life.Token);
            var text = run.Output ?? "";
            server.Post(text);
            message.Output?.Add(FakeDiscord.CreateMessage(text), run.Kind == RunKind.Error);
            if (run.BlockMs > 0)
                await Task.Delay(run.BlockMs, server.Life.Token);
        }
        catch (OperationCanceledException)
        {
        }

        return new(run.Kind == RunKind.Error ? CommandRunStatus.Error : CommandRunStatus.Ran, run.Reason);
    }
}

public sealed class EvalServices
{
    public required EmbeddingService Embedder { get; init; }
    public required CommandSearchService Search { get; init; }
    public required DataToolSearchService DataSearch { get; init; }
    public required IBotCredsProvider Creds { get; init; }

    public static async Task<EvalServices> CreateAsync(string commandListPath)
    {
        var embedder = new EmbeddingService(new FakeHttpFactory(new NoNetworkHandler()));
        await embedder.EnsureModelReadyAsync();
        if (!embedder.IsReady)
            throw new InvalidOperationException("The embedding model did not load.");

        var botCreds = Substitute.For<IBotCreds>();
        botCreds.AiApiKey.Returns("eval-key");
        botCreds.TotalShards.Returns(1);
        var creds = Substitute.For<IBotCredsProvider>();
        creds.GetCreds().Returns(botCreds);

        var search = new CommandSearchService(
            embedder,
            new ShardData(new DiscordSocketClient(), botCreds),
            Substitute.For<IPubSub>());
        await search.IndexCommandListAsync(await File.ReadAllBytesAsync(commandListPath));
        if (!search.IsReady)
            throw new InvalidOperationException("The command index did not build.");

        IAiToolRegistry? registry = null;
        var dataSearch = new DataToolSearchService(embedder, new Lazy<IAiToolRegistry>(() => registry!));
        registry = new AiToolRegistry([]);

        return new()
        {
            Embedder = embedder,
            Search = search,
            DataSearch = dataSearch,
            Creds = creds
        };
    }

    public static AiAgentConfig CreateConfig()
        => new()
        {
            Enabled = true,
            ApiUrl = "http://model.eval",
            ModelName = "deepseek-v4.1-flash",
            MaxToolCalls = 10,
            ReasoningEffort = ""
        };

    public IReadOnlyList<IAiTool> CreateTools(FakeServer server)
    {
        var cmdHandler = Substitute.For<ICommandHandler>();
        cmdHandler.GetPrefix(Arg.Any<IGuild>()).Returns(".");

        var commands = new CommandsAiAdapter(cmdHandler, new FakeCommandRunner(server), Search);

        IAiToolRegistry? registry = null;
        var discovery = new DiscoveryAiAdapter(Search, DataSearch, new Lazy<IAiToolRegistry>(() => registry!));

        var tools = new List<IAiTool>
        {
            new CommandsAiAdapter_RunCommand_AiTool(commands),
            new DiscoveryAiAdapter_SearchCommands_AiTool(discovery),
            new DiscoveryAiAdapter_SearchDataTools_AiTool(discovery),
            new DiscoveryAiAdapter_InvokeDataTool_AiTool(discovery),
            new FakeSendMessageTool(server),
            new FakeAskTool(server),
            new FakeGetMessageTool()
        };

        registry = new AiToolRegistry(tools);
        return tools;
    }
}

public sealed record LoopOutcome(
    string Id,
    int ModelRequests,
    int ToolsOffRequests,
    ServerSnapshot Server,
    string? Final,
    bool FinalPosted,
    string Ending,
    bool PrefixStable,
    long ElapsedMs)
{
    public int Runs(string name)
        => Server.Executed.GetValueOrDefault(name);

    public int TotalRuns
        => Server.Executed.Values.Sum();

    public int Messages
        => Server.CommandMessages.Count + Server.AgentMessages.Count + (FinalPosted ? 1 : 0);
}

public static class AgentHarness
{
    private const string SYSTEM = "You are Nadeko, a Discord bot. Use the tools to do what the member asks.";
    private const string CONTEXT = "Server: Test server (100). Channel: #general (200). Member: member (300).";
    private const string TURN = "Current time: 2026-11-16 12:00 UTC.";

    public static async Task<LoopOutcome> RunAsync(Scenario scenario, EvalServices services)
    {
        using var server = new FakeServer(FakeCommands.All());
        using var model = new ScriptedModel(scenario.Script, scenario.ToolsOffReply);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var session = new AiAgentSession(new FakeHttpFactory(model), services.Creds, new BotPolicy(false));
        var tools = services.CreateTools(server);
        var registry = new AiToolRegistry(tools);

        // The requests of one scenario are one conversation of one member, as AiAgentService keeps it.
        var conversation = new AgentConversation(Environment.TickCount64);
        string[] requests = [scenario.Request, ..scenario.FollowUps ?? []];

        string? final = null;
        var ending = "";
        var sw = Stopwatch.StartNew();
        foreach (var request in requests)
        {
            model.StartRequest();
            var question = conversation.TakeQuestion();
            var ctx = FakeDiscord.CreateContext(request, question?.Request, conversation, cts.Token);
            conversation.Begin(ctx);

            // The untrusted message reaches the session through the channel history, as in the bot.
            ChannelHistoryFeed? history = null;
            if (scenario.Untrusted is { } untrusted)
            {
                var buffer = new ChannelMessageBuffer(10);
                buffer.Push(new(399, 666, "bob", untrusted, DateTimeOffset.UtcNow));
                history = new(buffer, 200, "general", 400);
            }

            var searchText = question is null ? request : question.Request + "\n" + request;
            var prompt = new AiAgentPrompt(
                SYSTEM,
                CONTEXT,
                history,
                TURN,
                AgentConversation.PromptFor(request, question) + AgentHitFormatter.Candidates(services.Search, searchText));

            final = null;
            try
            {
                var result = await session.RunAsync(
                    prompt,
                    ctx,
                    tools,
                    registry.GetToolSchemas(),
                    EvalServices.CreateConfig(),
                    null,
                    cts.Token);

                if (result.TryPickT0(out var ok, out var failure))
                {
                    final = ok.Response;
                    ending = ok.AskPending ? "ask" : "reply";
                }
                else
                {
                    final = "failure: " + failure;
                    ending = "error";
                }
            }
            catch (OperationCanceledException)
            {
                ending = "cancelled";
            }
            catch (Exception ex)
            {
                final = "exception: " + ex.GetType().Name;
                ending = "exception";
            }
            finally
            {
                conversation.End(ctx);
            }

            if (ending is "cancelled" or "exception")
                break;
        }

        sw.Stop();
        // A command which runs on in the background can still post, as it would in a real channel.
        await Task.Delay(300);
        await server.Life.CancelAsync();

        var posted = ending is "reply" or "error" && !string.IsNullOrWhiteSpace(final);
        if (!posted && ending == "reply")
            ending = "silent";

        return new(
            scenario.Id,
            model.Requests,
            model.ToolsOffRequests,
            server.Snapshot(),
            final,
            posted,
            ending,
            model.PrefixStable,
            sw.ElapsedMilliseconds);
    }
}
