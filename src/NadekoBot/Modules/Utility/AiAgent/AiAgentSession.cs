using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OneOf;
using Serilog.Events;

namespace NadekoBot.Modules.Utility.AiAgent;

public readonly record struct AiProviderInfo(string Url, string AuthValue);

public sealed class AiAgentSession(
    IHttpClientFactory httpFactory,
    IBotCredsProvider credsProvider,
    BotPolicy policy) : IAiAgentSession, INService
{
    private const int MAX_REPLY_BYTES = 4 * 1024 * 1024;
    private const int MAX_ERROR_BODY_BYTES = 16 * 1024;
    private const int MAX_ERROR_LOG_CHARS = 300;

    // OpenRouter and Mistral wrap the error of the provider in their own error.
    private const int MAX_ERROR_DEPTH = 2;
    private const int MAX_ERROR_ITEMS = 5;

    // A reply with more calls is a model in a loop; the rest get a refusal, so each call still has its tool message.
    private const int MAX_CALLS_PER_REPLY = 8;

    // Shards which got a 429 at the same time must not all retry at the same moment.
    private const int MAX_RETRY_JITTER_MS = 500;

    private static readonly TimeSpan[] _retryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
    private static readonly TimeSpan _maxRetryAfter = TimeSpan.FromSeconds(10);

    // A provider which hangs gets another attempt instead of the whole time of the request.
    private static readonly TimeSpan _attemptTimeout = TimeSpan.FromSeconds(60);

    public const int MAX_CONCURRENT_MODEL_CALLS = 8;
    private static readonly SemaphoreSlim _modelCalls = new(MAX_CONCURRENT_MODEL_CALLS, MAX_CONCURRENT_MODEL_CALLS);

    // Optional fields which some models refuse.
    private static readonly string[] _optionalParams = ["max_completion_tokens", "max_tokens", "temperature", "reasoning_effort"];

    private static readonly SearchValues<string> _refusalWords = SearchValues.Create(
        [
            "unsupported", "not supported", "unrecognized", "unknown", "does not support", "doesn't support", "isn't supported",
            "extra inputs", "unexpected keyword"
        ],
        StringComparison.OrdinalIgnoreCase);

    // A value error can use these words too, so they never drop a token limit, which would leave the output without a limit.
    private static readonly SearchValues<string> _weakRefusalWords = SearchValues.Create(
        ["not allowed", "not permitted"],
        StringComparison.OrdinalIgnoreCase);

    // Keyed by the settings snapshot, so a change of the settings sends every field again.
    private DroppedParams? _dropped;

    private static readonly FrozenSet<string> _searchTools = FrozenSet.ToFrozenSet(
        ["search_commands", "search_data_tools", "web_search", "search_messages"],
        StringComparer.Ordinal);

    private static readonly FrozenSet<string> _messageTools = FrozenSet.ToFrozenSet(
        ["send_message", "edit_message", "delete_message"],
        StringComparer.Ordinal);

    // Their results hold text which other people wrote, so a command which only that text asks for does not run.
    private static readonly FrozenSet<string> _untrustedResultTools = FrozenSet.ToFrozenSet(
        ["get_message", "search_messages", "web_search", "invoke_data_tool"],
        StringComparer.Ordinal);

    private const string LAST_STEP_NOTE = """


        [Last step: tools are off. In two or three sentences, tell the member what you did, what you did not do, and why.]
        """;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<OneOf<AiAgentResult, AgentFailure>> RunAsync(
        AiAgentPrompt prompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        IAgentProgress? progress = null,
        CancellationToken ct = default)
    {
        var run = new SessionRun();
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await RunLoopInternalAsync(prompt, context, tools, toolSchemas, config, progress, run, ct);
            run.Ending ??= result.Match(
                static r => r.AskPending ? "ask" : r.Response.Length == 0 ? "silent" : "reply",
                static f => f.ToString());
            return result;
        }
        catch (OperationCanceledException)
        {
            run.Ending = "cancelled";
            throw;
        }
        catch
        {
            run.Ending = "exception";
            throw;
        }
        finally
        {
            Log.Information(
                "AI agent request in {GuildId} by {UserId}: {Ending} after {Requests} model requests and {ToolCalls} tool calls "
                + "in {ElapsedMs} ms, {PromptTokens} prompt tokens ({CachedTokens} cached). Steps: {Steps}",
                context.Guild.Id,
                context.User.Id,
                run.Ending,
                run.Requests,
                run.ToolCalls,
                sw.ElapsedMilliseconds,
                run.PromptTokens,
                run.CachedTokens,
                run.Steps.ToString());
        }
    }

    // On a managed bot these could reroute the request or impersonate a different client of the provider.
    private static bool IsReservedHeaderInternal(string name)
        => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    // Lets the provider keep one session for each channel, for prompt caching.
    private static string ReplaceHeaderPlaceholdersInternal(string value, AiToolContext context)
    {
        if (!value.Contains('%'))
            return value;

        if (value.Contains("%server.id%", StringComparison.Ordinal))
            value = value.Replace("%server.id%", context.Guild.Id.ToString(), StringComparison.Ordinal);
        if (value.Contains("%channel.id%", StringComparison.Ordinal))
            value = value.Replace("%channel.id%", context.SourceChannel.Id.ToString(), StringComparison.Ordinal);
        if (value.Contains("%user.id%", StringComparison.Ordinal))
            value = value.Replace("%user.id%", context.User.Id.ToString(), StringComparison.Ordinal);

        return value;
    }

    private sealed class SessionRun
    {
        public int Requests;
        public int ToolCalls;
        public long PromptTokens;
        public long CachedTokens;
        public string? Ending;
        public readonly StringBuilder Steps = new();
    }

    private readonly record struct ToolCallOutcome(
        string Name,
        JsonElement Args,
        string Result,
        bool Ok,
        bool Final,
        string Status,
        string? Detail);

    private async Task<OneOf<AiAgentResult, AgentFailure>> RunLoopInternalAsync(
        AiAgentPrompt prompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        IAgentProgress? progress,
        SessionRun run,
        CancellationToken ct)
    {
        var provider = ResolveProviderInternal(config);
        var reasoningEffort = AiAgentConfigService.GetReasoningEffort(config.ReasoningEffort);
        var state = context.Session;

        var toolMap = new Dictionary<string, IAiTool>(tools.Count, StringComparer.Ordinal);
        foreach (var tool in tools)
            toolMap[tool.Name] = tool;

        var messages = new List<AgentChatMessage>
        {
            new()
            {
                Role = "system",
                Content = prompt.System
            }
        };

        // One untrusted text for each message, so the words of one member and the id of another never form one request.
        var untrusted = new List<string>();
        var history = prompt.History?.GetSnapshot(untrusted);
        foreach (var text in untrusted)
            state.AddUntrusted(text, inPrompt: true);

        // Two messages, because OpenRouter keeps a conversation on one provider by the first user message, and the history changes with each message.
        messages.Add(new()
        {
            Role = "user",
            Content = prompt.Context
        });

        if (history is not null)
        {
            messages.Add(new()
            {
                Role = "user",
                Content = history
            });
        }

        messages.Add(new()
        {
            Role = "user",
            Content = prompt.Turn
        });

        messages.Add(new()
        {
            Role = "user",
            Content = prompt.Request
        });

        var useModelList = config.Models is { Count: > 0 };
        var requestModel = useModelList ? null : config.ModelName;
        var requestModels = useModelList ? config.Models : null;
        var requestTools = toolSchemas.Count > 0 ? toolSchemas.ToList() : null;

        var failuresInRow = 0;
        var forceLast = false;

        for (var step = 0;; step++)
        {
            ct.ThrowIfCancellationRequested();

            // maxToolCalls counts the steps with tools. One more request without tools lets the model sum up.
            var lastStep = forceLast || step >= config.MaxToolCalls;

            var toolChoice = lastStep && requestTools is not null ? "none" : null;
            var response = await CallLlmInternalAsync(
                provider,
                config,
                context,
                new(requestModel, requestModels, messages, requestTools, toolChoice, reasoningEffort),
                ct);
            if (response is null)
                return AgentFailure.Provider;

            run.Requests++;
            if (response.Usage is { } u)
            {
                run.PromptTokens += u.PromptTokens ?? 0;
                run.CachedTokens += u.PromptCacheHitTokens ?? u.PromptTokensDetails?.CachedTokens ?? 0;
            }

            var choice = response.Choices?.FirstOrDefault();
            if (choice?.Message is null)
                return AgentFailure.EmptyReply;

            var cutOff = string.Equals(choice.FinishReason, "length", StringComparison.Ordinal);
            var assistantMsg = WithValidToolCallsInternal(choice.Message, step);
            var hasCalls = assistantMsg.ToolCalls is { Count: > 0 };

            // A call list which stops at the token limit can miss calls or hold half an argument.
            if (cutOff && hasCalls && !lastStep)
            {
                Log.Warning(
                    "AI agent reply with tool calls stopped at the token limit of {MaxTokens}, so no tool ran",
                    config.MaxTokens);
                run.Steps.Append(step + 1).Append(":cut-off");
                return AgentFailure.CutOff;
            }

            if (!hasCalls || lastStep)
            {
                if (cutOff && string.IsNullOrWhiteSpace(assistantMsg.Content))
                    return AgentFailure.CutOff;

                var summary = lastStep && step > 0;
                run.Steps.Append(step + 1).Append(summary ? ":summary" : ":reply");
                return FinishInternal(assistantMsg.Content, context, run, summary);
            }

            messages.Add(assistantMsg);

            if (progress is not null && !string.IsNullOrWhiteSpace(assistantMsg.Content))
                progress.OnNote(assistantMsg.Content);

            var allFinal = true;
            var anyFinal = false;
            var calls = assistantMsg.ToolCalls!;

            var askAt = context.EarlierRequest is null ? IndexOfAskInternal(calls) : -1;
            for (var i = 0; i < calls.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var toolCall = calls[i];

                if (askAt >= 0 && i != askAt)
                {
                    allFinal = false;
                    run.Steps.Append(step + 1).Append(":waits_for_answer; ");
                    messages.Add(new()
                    {
                        Role = "tool",
                        ToolCallId = toolCall.Id,
                        Content = "Not run: this reply also calls ask_user, and a question runs alone. "
                                  + "Send this call again after the member answers, or in a reply without ask_user."
                    });
                    continue;
                }

                if (i >= MAX_CALLS_PER_REPLY)
                {
                    allFinal = false;
                    run.Steps.Append(step + 1).Append(":over_reply_limit; ");
                    messages.Add(new()
                    {
                        Role = "tool",
                        ToolCallId = toolCall.Id,
                        Content = $"Not run: one reply may hold at most {MAX_CALLS_PER_REPLY} tool calls. "
                                  + "Send the calls which are still needed in your next reply."
                    });
                    continue;
                }

                run.ToolCalls++;

                var callTime = Stopwatch.StartNew();
                state.BeginCall();
                var outcome = await RunToolCallInternalAsync(toolCall, toolMap, tools, context, state, ct);

                progress?.OnToolCall(outcome.Name, outcome.Args, !outcome.Ok);

                failuresInRow = outcome.Ok ? 0 : failuresInRow + 1;

                // A copy of a call which already ran neither blocks nor allows the end of the request.
                if (outcome.Status != AgentSessionState.DUPLICATE_STATUS)
                {
                    allFinal &= outcome.Ok && outcome.Final;
                    anyFinal |= outcome.Ok && outcome.Final;
                }

                AppendStepInternal(run.Steps, step + 1, outcome, callTime.ElapsedMilliseconds);

                // A tool result can hold text from members or web pages, which must not pose as a <channel_update>.
                var content = PromptSanitizer.Sanitize(outcome.Result);
                if (_untrustedResultTools.Contains(outcome.Name) || toolMap.GetValueOrDefault(outcome.Name)?.IsDataTool == true)
                    state.AddToolResult(content);

                messages.Add(new()
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = content
                });
            }

            if (context.AskPending)
            {
                return new AiAgentResult
                {
                    Response = "",
                    ToolCallCount = run.ToolCalls,
                    AskPending = true
                };
            }

            if (allFinal && anyFinal)
            {
                run.Ending = "final";
                return new AiAgentResult
                {
                    Response = "",
                    ToolCallCount = run.ToolCalls
                };
            }

            if (failuresInRow >= AgentSessionState.MAX_FAILURES_IN_A_ROW || state.LimitReached)
                forceLast = true;

            var nextIsLast = forceLast || step + 1 >= config.MaxToolCalls;

            // Goes into the last tool result, because some OpenAI-compatible APIs reject a user message after tool messages.
            untrusted.Clear();
            var update = prompt.History?.GetUpdate(untrusted);
            foreach (var text in untrusted)
                state.AddUntrusted(text, inPrompt: true);

            if (update is not null || nextIsLast)
            {
                var last = messages[^1];
                var content = last.Content;
                if (update is not null)
                    content += "\n\n" + update;
                if (nextIsLast)
                    content += LAST_STEP_NOTE;

                messages[^1] = new()
                {
                    Role = last.Role,
                    ToolCallId = last.ToolCallId,
                    Content = content
                };
            }
        }
    }

    private static OneOf<AiAgentResult, AgentFailure> FinishInternal(
        string? content,
        AiToolContext context,
        SessionRun run,
        bool summary)
    {
        var state = context.Session;
        var text = content?.Trim() ?? "";

        if (text.Length == 0 && !state.AnythingShown && !state.RanQuietly)
            return AgentFailure.EmptyReply;

        return new AiAgentResult
        {
            Response = text,
            ToolCallCount = run.ToolCalls,
            LimitReached = summary
        };
    }

    private static int IndexOfAskInternal(List<AgentToolCall> calls)
    {
        for (var i = 0; i < calls.Count; i++)
        {
            if (calls[i].Function?.Name == Adapters.SessionAiAdapter.ASK_TOOL)
                return i;
        }

        return -1;
    }

    private static async Task<ToolCallOutcome> RunToolCallInternalAsync(
        AgentToolCall call,
        Dictionary<string, IAiTool> toolMap,
        IReadOnlyList<IAiTool> tools,
        AiToolContext context,
        AgentSessionState state,
        CancellationToken ct)
    {
        var name = call.Function?.Name ?? string.Empty;

        // The model chose this name, so it stays out of the trace log and the live message.
        if (!toolMap.TryGetValue(name, out var tool))
            return FailedInternal("unknown", default, UnknownToolResultInternal(name, tools), "unknown_tool");

        JsonElement args = default;
        var raw = call.Function?.Arguments;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                args = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return FailedInternal(name, default, InvalidArgsResultInternal(tool, "they are not valid JSON"), "invalid_json");
            }
        }

        if (args.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
            return FailedInternal(name, default, InvalidArgsResultInternal(tool, "they are not one JSON object"), "invalid_json");

        if (_searchTools.Contains(name) && !state.TryUseSearch(name))
        {
            state.LimitReached = true;
            return FailedInternal(
                name,
                args,
                $"Error: you already used {name} {AgentSessionState.MAX_SEARCHES_PER_TOOL} times in this request. "
                + "Use what you found, or tell the member that you did not find it.",
                "limit");
        }

        string? key = null;
        if (_messageTools.Contains(name))
        {
            key = name + " " + (args.ValueKind == JsonValueKind.Object ? JsonSerializer.Serialize(args) : "{}");
            if (state.FindMessageAction(key) is { } earlier)
            {
                return new(
                    name,
                    args,
                    "Not done again: you made this exact call before in this request. The earlier result was: "
                    + earlier,
                    false,
                    false,
                    AgentSessionState.DUPLICATE_STATUS,
                    null);
            }

            if (!state.TryUseMessageAction())
            {
                state.LimitReached = true;
                return FailedInternal(
                    name,
                    args,
                    $"Error: this request already sent, edited or deleted {AgentSessionState.MAX_MESSAGE_ACTIONS} messages, which is the limit.",
                    "limit");
            }
        }

        string result;
        try
        {
            result = await tool.ExecuteAsync(context, args);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error executing AI tool {ToolName}", name);
            result = "Error: the tool failed. Try another way, or tell the member that it failed.";
        }

        var report = state.Report;
        var ok = report?.Ok ?? !IsErrorResultInternal(result);
        if (key is not null)
        {
            state.RecordMessageAction(key, result);
            if (ok)
                state.AnythingShown = true;
        }

        return new(name, args, result, ok, report?.Final ?? false, report?.Status ?? (ok ? "ok" : "failed"), report?.Detail);
    }

    private static ToolCallOutcome FailedInternal(string name, JsonElement args, string result, string status)
        => new(name, args, result, false, false, status, null);

    private static string UnknownToolResultInternal(string name, IReadOnlyList<IAiTool> tools)
    {
        var sb = new StringBuilder("Error: there is no tool named '")
            .Append(name)
            .Append("'. The tools are: ");

        var first = true;
        foreach (var tool in tools)
        {
            if (tool.IsDataTool)
                continue;

            if (!first)
                sb.Append(", ");

            sb.Append(tool.Name);
            first = false;
        }

        sb.Append(". A data tool runs through invoke_data_tool.");
        return sb.ToString();
    }

    // Shows the shape which the tool expects, because a small model often repeats its broken call otherwise.
    private static string InvalidArgsResultInternal(IAiTool tool, string why)
    {
        var sb = new StringBuilder("Error: the bot did not run ")
            .Append(tool.Name)
            .Append(", because its arguments were not used: ")
            .Append(why)
            .Append(". Send the call again with one JSON object, for example {");

        var schema = tool.ParameterSchema;
        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in req.EnumerateArray())
                {
                    if (r.GetString() is { } s)
                        required.Add(s);
                }
            }

            var first = true;
            foreach (var prop in props.EnumerateObject())
            {
                if (required.Count > 0 && !required.Contains(prop.Name))
                    continue;

                if (!first)
                    sb.Append(", ");

                sb.Append('"').Append(prop.Name).Append("\": ").Append(ExampleValueInternal(prop.Value));
                first = false;
            }
        }

        return sb.Append("}.").ToString();
    }

    private static string ExampleValueInternal(JsonElement prop)
        => prop.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() switch
            {
                "integer" or "number" => "0",
                "boolean" => "false",
                "array" => "[]",
                "object" => "{}",
                _ => "\"...\""
            }
            : "\"...\"";

    private static void AppendStepInternal(StringBuilder sb, int step, ToolCallOutcome outcome, long ms)
    {
        sb.Append(step).Append(':').Append(outcome.Name);
        if (outcome.Detail is { } detail)
            sb.Append('(').Append(detail).Append(')');

        sb.Append('=').Append(outcome.Status).Append(' ').Append(ms).Append("ms; ");
    }

    // The next request sends this message back, and OpenAI-compatible APIs need the id, the name and the arguments of each tool call.
    private static AgentChatMessage WithValidToolCallsInternal(AgentChatMessage msg, int step)
    {
        if (msg.ToolCalls is not { Count: > 0 } calls)
            return msg;

        var valid = true;
        foreach (var call in calls)
        {
            if (call is not { Id: not null, Function: { Name.Length: > 0, Arguments: not null } })
            {
                valid = false;
                break;
            }
        }

        // OpenRouter reads reasoning_details or the reasoning text, so both would send the reasoning twice.
        var onlyDetails = HasReasoningDetailsInternal(msg.ReasoningDetails)
                          && (msg.Reasoning is not null || msg.ReasoningContent is not null);
        if (valid && !onlyDetails)
            return msg;

        var validCalls = new List<AgentToolCall>(calls.Count);
        for (var i = 0; i < calls.Count; i++)
        {
            if (calls[i] is not { Function: { Name: { Length: > 0 } name } function } call)
                continue;

            validCalls.Add(new()
            {
                Id = call.Id ?? $"call_{step}_{i}",
                Type = call.Type ?? "function",
                Function = new()
                {
                    Name = name,
                    Arguments = function.Arguments ?? "{}"
                }
            });
        }

        return new()
        {
            Role = msg.Role,
            Content = msg.Content,
            Name = msg.Name,
            ToolCalls = validCalls.Count > 0 ? validCalls : null,
            ToolCallId = msg.ToolCallId,
            ReasoningContent = onlyDetails ? null : msg.ReasoningContent,
            Reasoning = onlyDetails ? null : msg.Reasoning,
            ReasoningDetails = msg.ReasoningDetails
        };
    }

    // An empty list holds no reasoning, so the text fields must stay.
    private static bool HasReasoningDetailsInternal(JsonElement? details)
        => details is { } d
           && d.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
           && (d.ValueKind != JsonValueKind.Array || d.GetArrayLength() > 0);

    // Adapters return "Error..." text, and generated tool wrappers return {"error":...} JSON.
    private static bool IsErrorResultInternal(string result)
        => result.StartsWith("Error", StringComparison.Ordinal)
           || result.StartsWith("{\"error\"", StringComparison.Ordinal);

    private AiProviderInfo ResolveProviderInternal(AiAgentConfig config)
    {
        var creds = credsProvider.GetCreds();
        return new(
            config.ApiUrl.TrimEnd('/') + "/v1/chat/completions",
            $"Bearer {creds.AiApiKey}");
    }

    private static AgentChatRequest BuildRequestInternal(AiAgentConfig config, DroppedParam dropped, in RequestParts parts)
    {
        var useMaxTokens = (dropped & DroppedParam.MaxCompletionTokens) != 0;
        var noTokenLimit = useMaxTokens && (dropped & DroppedParam.MaxTokens) != 0;
        return new()
        {
            Model = parts.Model,
            Models = parts.Models,
            Messages = parts.Messages,
            Tools = parts.Tools,
            ToolChoice = parts.ToolChoice,
            MaxCompletionTokens = useMaxTokens ? null : config.MaxTokens,
            MaxTokens = useMaxTokens && !noTokenLimit ? config.MaxTokens : null,
            Temperature = (dropped & DroppedParam.Temperature) != 0 ? null : config.Temperature,
            ReasoningEffort = (dropped & DroppedParam.ReasoningEffort) != 0 ? null : parts.ReasoningEffort
        };
    }

    private DroppedParam GetDroppedInternal(AiAgentConfig config)
        => Volatile.Read(ref _dropped) is { } d && ReferenceEquals(d.Config, config) ? d.Params : DroppedParam.None;

    // The field to leave out of the next request, or None when the failed request already left it out.
    private DroppedParam DropParamInternal(AiAgentConfig config, string name, DroppedParam used, string model)
    {
        var flag = name switch
        {
            "max_completion_tokens" => DroppedParam.MaxCompletionTokens,
            "max_tokens" => DroppedParam.MaxTokens | DroppedParam.MaxCompletionTokens,
            "temperature" => DroppedParam.Temperature,
            "reasoning_effort" => DroppedParam.ReasoningEffort,
            _ => DroppedParam.None
        };

        if (flag == DroppedParam.None || (used & flag) == flag)
            return DroppedParam.None;

        while (true)
        {
            var current = Volatile.Read(ref _dropped);
            var before = current is { } c && ReferenceEquals(c.Config, config) ? c.Params : DroppedParam.None;
            if ((before | flag) == before)
                return flag;

            if (!ReferenceEquals(Interlocked.CompareExchange(ref _dropped, new DroppedParams(config, before | flag), current), current))
                continue;

            Log.Warning("The AI provider refused the field {Field} for model {Model}. The agent sends its requests without it",
                name,
                model);
            return flag;
        }
    }

    // Only the message counts: the type "invalid_request_error" or a quoted request would make every 400 a refusal.
    private static string? RefusedParamInternal(List<(string Message, string? Param)> errors)
    {
        foreach (var (message, param) in errors)
        {
            var strong = message.AsSpan().IndexOfAny(_refusalWords) >= 0;
            if (!strong && message.AsSpan().IndexOfAny(_weakRefusalWords) < 0)
                continue;

            var name = FieldOfParamInternal(param) ?? FirstFieldInternal(message);
            if (name is null || (!strong && name is "max_completion_tokens" or "max_tokens"))
                continue;

            return name;
        }

        return null;
    }

    private static string? FieldOfParamInternal(string? param)
    {
        if (param is null)
            return null;

        foreach (var name in _optionalParams)
        {
            if (param.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }

    // The first name counts, because the message can name the replacement too.
    private static string? FirstFieldInternal(string message)
    {
        string? first = null;
        var firstAt = int.MaxValue;
        foreach (var name in _optionalParams)
        {
            var at = IndexOfFieldInternal(message, name);
            if (at >= 0 && at < firstAt)
            {
                first = name;
                firstAt = at;
            }
        }

        return first;
    }

    // A whole field name only, so "max_tokens" does not match inside a longer name such as "max_tokens_per_minute".
    private static int IndexOfFieldInternal(string message, string name)
    {
        var from = 0;
        while (from < message.Length)
        {
            var at = message.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return -1;

            var end = at + name.Length;
            if ((at == 0 || !IsFieldCharInternal(message[at - 1])) && (end == message.Length || !IsFieldCharInternal(message[end])))
                return at;

            from = at + 1;
        }

        return -1;
    }

    private static bool IsFieldCharInternal(char c)
        => char.IsAsciiLetterOrDigit(c) || c == '_';

    // A body which is not JSON is the message itself.
    private static void ReadErrorTextInternal(string text, int depth, List<(string, string?)> errors, out string? shape)
    {
        shape = null;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            errors.Add((text, null));
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            ReadErrorElementInternal(root, depth, errors);
            if (depth == 0 && errors.Count == 0)
                shape = ShapeInternal(root);
        }
    }

    // OpenAI-style errors, OpenRouter metadata.raw (text or object), a Mistral message object, FastAPI detail[] and a list of errors.
    private static void ReadErrorElementInternal(JsonElement element, int depth, List<(string, string?)> errors)
    {
        if (element.ValueKind == JsonValueKind.Array && depth == 0)
        {
            var items = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (++items > MAX_ERROR_ITEMS)
                    break;

                ReadErrorElementInternal(item, depth + 1, errors);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String)
                errors.Add((error.GetString() ?? "", null));
            else if (error.ValueKind == JsonValueKind.Object)
            {
                ReadMessageInternal(error, depth, errors);
                if (depth < MAX_ERROR_DEPTH
                    && error.TryGetProperty("metadata", out var meta)
                    && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("raw", out var raw))
                {
                    if (raw.ValueKind == JsonValueKind.String)
                        ReadErrorTextInternal(raw.GetString() ?? "", depth + 1, errors, out _);
                    else
                        ReadErrorElementInternal(raw, depth + 1, errors);
                }
            }
        }

        ReadMessageInternal(element, depth, errors);

        if (!element.TryGetProperty("detail", out var detail))
            return;

        if (detail.ValueKind == JsonValueKind.String)
        {
            errors.Add((detail.GetString() ?? "", null));
            return;
        }

        if (detail.ValueKind != JsonValueKind.Array)
            return;

        var details = 0;
        foreach (var item in detail.EnumerateArray())
        {
            if (++details > MAX_ERROR_ITEMS)
                break;

            if (item.ValueKind != JsonValueKind.Object || TryGetStringInternal(item, "msg") is not { } msg)
                continue;

            string? field = null;
            if (item.TryGetProperty("loc", out var loc) && loc.ValueKind == JsonValueKind.Array && loc.GetArrayLength() > 0)
            {
                var last = loc[loc.GetArrayLength() - 1];
                field = last.ValueKind == JsonValueKind.String ? last.GetString() : null;
            }

            errors.Add((msg, field));
        }
    }

    private static void ReadMessageInternal(JsonElement element, int depth, List<(string, string?)> errors)
    {
        if (!element.TryGetProperty("message", out var message))
            return;

        if (message.ValueKind == JsonValueKind.String)
            errors.Add((message.GetString() ?? "", TryGetStringInternal(element, "param")));
        else if (message.ValueKind == JsonValueKind.Object && depth < MAX_ERROR_DEPTH)
            ReadErrorElementInternal(message, depth + 1, errors);
    }

    // The log gets the names of the fields, not their values, which can echo the request.
    private static string ShapeInternal(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "<JSON " + root.ValueKind + " without a message>";

        var sb = new StringBuilder("<JSON without a message, fields:");
        foreach (var property in root.EnumerateObject())
            sb.Append(' ').Append(property.Name);

        return sb.Append('>').ToString();
    }

    private static string? TryGetStringInternal(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // A request which the provider did not take (rate limit, overload, timeout) changed nothing, so it is safe to send again.
    private async Task<AgentChatResponse?> CallLlmInternalAsync(
        AiProviderInfo provider,
        AiAgentConfig config,
        AiToolContext context,
        RequestParts parts,
        CancellationToken ct)
    {
        // Each request without a refused field adds one field, so this loop ends after at most four of them.
        var learned = DroppedParam.None;
        for (var attempt = 0;; attempt++)
        {
            var used = GetDroppedInternal(config) | learned;
            var request = BuildRequestInternal(config, used, parts);
            var (response, retry, retryAfter, refused) = await SendOnceInternalAsync(provider, config, context, request, ct);
            if (response is not null)
                return response;

            if (refused is not null
                && DropParamInternal(config, refused, used, parts.Model ?? string.Join(", ", parts.Models ?? [])) is var dropped
                and not DroppedParam.None)
            {
                learned |= dropped;
                // A refused field is not one of the retries.
                attempt--;
                continue;
            }

            if (!retry)
                return null;

            // A provider which asks for a longer pause would refuse an earlier retry too.
            if (attempt >= _retryDelays.Length || retryAfter > _maxRetryAfter)
            {
                Log.Error(
                    "AI agent API {Url} failed, and the bot stopped after {Retries} retries (Retry-After {RetryAfter})",
                    provider.Url,
                    attempt,
                    retryAfter?.ToString() ?? "none");
                return null;
            }

            var delay = retryAfter is { } wait && wait > TimeSpan.Zero ? wait : _retryDelays[attempt];
            delay += TimeSpan.FromMilliseconds(Random.Shared.Next(MAX_RETRY_JITTER_MS));

            Log.Warning(
                "AI agent API {Url} did not answer, retry {Attempt} in {DelayMs} ms",
                provider.Url,
                attempt + 1,
                (int)delay.TotalMilliseconds);

            await Task.Delay(delay, ct);
        }
    }

    // 520-524 and 529 come from Cloudflare and overloaded providers; 501 and 505 never change on a retry.
    private static bool IsRetryableInternal(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
           || (int)status is >= 500 and < 600 and not 501 and not 505;

    private async Task<(AgentChatResponse? Response, bool Retry, TimeSpan? RetryAfter, string? Refused)> SendOnceInternalAsync(
        AiProviderInfo provider,
        AiAgentConfig config,
        AiToolContext context,
        AgentChatRequest request,
        CancellationToken ct)
    {
        using var http = httpFactory.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", provider.AuthValue);

        if (config.CustomHeaders is { Count: > 0 } headers)
        {
            foreach (var (key, value) in headers)
            {
                if (policy.Managed && IsReservedHeaderInternal(key))
                    continue;

                http.DefaultRequestHeaders.TryAddWithoutValidation(key, ReplaceHeaderPlaceholdersInternal(value, context));
            }
        }

        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _modelCalls.WaitAsync(ct);
        try
        {
            attemptCts.CancelAfter(_attemptTimeout);
            var token = attemptCts.Token;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, provider.Url);
            httpRequest.Content = JsonContent.Create(request);
            using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                return await ReadFailureInternalAsync(provider, response, token, ct);

            if (await ExternalHttp.ReadLimitedAsync(response.Content, MAX_REPLY_BYTES, token) is not { } body)
            {
                Log.Error("AI agent API {Url} sent a reply larger than {Max} bytes", provider.Url, MAX_REPLY_BYTES);
                return (null, false, null, null);
            }

            try
            {
                return (JsonSerializer.Deserialize<AgentChatResponse>(body.Buffer.AsSpan(0, body.Length), _jsonOpts), false, null, null);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(body.Buffer, clearArray: true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            SiteAnswer.LogUnreadable(provider.Url, ex);
            return (null, false, null, null);
        }
        catch (OperationCanceledException) when (attemptCts.IsCancellationRequested)
        {
            Log.Warning("AI agent API {Url} sent no answer within {Seconds} s", provider.Url, (int)_attemptTimeout.TotalSeconds);
            return (null, true, null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Log.Warning(ex, "AI agent API {Url} did not answer", provider.Url);
            return (null, true, null, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to call AI agent API at {Url}", provider.Url);
            return (null, false, null, null);
        }
        finally
        {
            _modelCalls.Release();
        }
    }

    // The status decides the retry, also when the body of the error cannot be read in time.
    private static async Task<(AgentChatResponse? Response, bool Retry, TimeSpan? RetryAfter, string? Refused)> ReadFailureInternalAsync(
        AiProviderInfo provider,
        HttpResponseMessage response,
        CancellationToken token,
        CancellationToken ct)
    {
        var retryable = IsRetryableInternal(response.StatusCode);
        string? errorText = null;
        try
        {
            if (await ExternalHttp.ReadLimitedAsync(response.Content, MAX_ERROR_BODY_BYTES, token) is { } error)
            {
                try
                {
                    errorText = Encoding.UTF8.GetString(error.Buffer, 0, error.Length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(error.Buffer, clearArray: true);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is OperationCanceledException or HttpRequestException or IOException)
        {
            // The status alone decides then.
        }

        var errors = new List<(string Message, string? Param)>(1);
        string? keys = null;
        if (errorText is not null)
        {
            try
            {
                ReadErrorTextInternal(errorText, 0, errors, out keys);
            }
            catch (InvalidOperationException)
            {
                // GetString throws on a lone surrogate escape of a cut body; the log keeps the messages read before it.
                if (errors.Count == 0)
                    keys = "<unreadable error body>";
            }
        }

        // A billing problem does not pass in a few seconds.
        if (response.StatusCode == HttpStatusCode.TooManyRequests
            && errorText?.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) == true)
            retryable = false;

        var refused = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
            ? RefusedParamInternal(errors)
            : null;

        Log.Write(
            retryable || refused is not null ? LogEventLevel.Warning : LogEventLevel.Error,
            "AI agent API {Url} returned {Status}: {Error}",
            provider.Url,
            (int)response.StatusCode,
            ErrorSummaryInternal(errorText is null, errors, keys));

        var retryAfter = response.Headers.RetryAfter is { } ra
            ? ra.Delta ?? (ra.Date is { } date ? date - DateTimeOffset.UtcNow : null)
            : null;

        return (null, retryable, retryAfter, refused);
    }

    private static string ErrorSummaryInternal(bool unreadable, List<(string Message, string? Param)> errors, string? keys)
    {
        if (unreadable)
            return "<unreadable or too long>";

        var text = keys ?? string.Join(" | ", errors.Select(static e => e.Message));
        if (string.IsNullOrWhiteSpace(text))
            return "<empty error message>";

        text = text.ReplaceLineEndings(" ");
        if (text.Length <= MAX_ERROR_LOG_CHARS)
            return text;

        var cut = MAX_ERROR_LOG_CHARS - 3;
        if (char.IsHighSurrogate(text[cut - 1]))
            cut--;

        return string.Concat(text.AsSpan(0, cut), "...");
    }

    private sealed class DroppedParams(AiAgentConfig config, DroppedParam @params)
    {
        public AiAgentConfig Config { get; } = config;

        public DroppedParam Params { get; } = @params;
    }

    private readonly record struct RequestParts(
        string? Model,
        List<string>? Models,
        List<AgentChatMessage> Messages,
        List<JsonElement>? Tools,
        string? ToolChoice,
        string? ReasoningEffort);

    [Flags]
    private enum DroppedParam
    {
        None = 0,
        Temperature = 1,
        ReasoningEffort = 2,

        // Then the request sends max_tokens, which older providers take.
        MaxCompletionTokens = 4,
        MaxTokens = 8
    }
}
