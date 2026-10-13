using System.Buffers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

public readonly record struct AiProviderInfo(string Url, string AuthValue);

public sealed class AiAgentSession(
    IHttpClientFactory httpFactory,
    IBotCredsProvider credsProvider,
    BotPolicy policy) : IAiAgentSession, INService
{
    private const int MAX_REPLY_BYTES = 4 * 1024 * 1024;
    private const int MAX_ERROR_LOG_BYTES = 2 * 1024;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // A request never changes a message which an earlier request sent, so the provider can reuse its prompt cache.
    public async Task<OneOf<AiAgentResult, Error<string>>> RunAsync(
        AiAgentPrompt prompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        IAgentProgress? progress = null,
        CancellationToken ct = default)
    {
        var usage = new SessionUsage();
        try
        {
            return await RunLoopInternalAsync(prompt, context, tools, toolSchemas, config, progress, usage, ct);
        }
        finally
        {
            Log.Debug(
                "AI agent session: {Steps} requests, {PromptTokens} prompt tokens, {CachedTokens} cached",
                usage.Requests,
                usage.PromptTokens,
                usage.CachedTokens);
        }
    }

    // On a managed bot these could reroute the request or impersonate a different client of the provider.
    private static bool IsReservedHeader(string name)
        => name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    private sealed class SessionUsage
    {
        public int Requests;
        public long PromptTokens;
        public long CachedTokens;
    }

    private async Task<OneOf<AiAgentResult, Error<string>>> RunLoopInternalAsync(
        AiAgentPrompt prompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        IAgentProgress? progress,
        SessionUsage usage,
        CancellationToken ct)
    {
        var provider = ResolveProviderInternal(config);

        var reasoningEffort = AiAgentConfigService.GetReasoningEffort(config.ReasoningEffort);

        var toolMap = tools.ToDictionary(t => t.Name);
        var messages = new List<AgentChatMessage>();

        messages.Add(new()
        {
            Role = "system",
            Content = prompt.System
        });

        var history = prompt.History?.GetSnapshot();
        messages.Add(new()
        {
            Role = "user",
            Content = history is null ? prompt.Context : prompt.Context + "\n" + history
        });

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

        var totalToolCalls = 0;

        var useModelList = config.Models is { Count: > 0 };
        var requestModel = useModelList ? null : config.ModelName;
        var requestModels = useModelList ? config.Models : null;
        var requestTools = toolSchemas.Count > 0 ? toolSchemas.ToList() : null;

        for (var step = 0; step < config.MaxToolCalls; step++)
        {
            ct.ThrowIfCancellationRequested();

            var request = new AgentChatRequest
            {
                Model = requestModel,
                Models = requestModels,
                Messages = messages,
                Tools = requestTools,
                MaxCompletionTokens = config.MaxTokens,
                Temperature = config.Temperature,
                ReasoningEffort = reasoningEffort
            };

            var response = await CallLlmInternalAsync(provider, config, request, ct);
            if (response is null)
                return new Error<string>("Failed to get response from AI provider.");

            usage.Requests++;
            if (response.Usage is { } u)
            {
                usage.PromptTokens += u.PromptTokens ?? 0;
                usage.CachedTokens += u.PromptTokensDetails?.CachedTokens ?? 0;
            }

            var choice = response.Choices?.FirstOrDefault();
            if (choice?.Message is null)
                return new Error<string>("Received empty response from AI provider.");

            var assistantMsg = choice.Message;
            messages.Add(assistantMsg);

            if (assistantMsg.ToolCalls is not { Count: > 0 })
            {
                if (string.Equals(choice.FinishReason, "length", StringComparison.Ordinal)
                    && string.IsNullOrWhiteSpace(assistantMsg.Content))
                {
                    return new Error<string>(
                        "The AI ran out of its token budget before replying (reasoning likely "
                        + "consumed it). Increase the `maxtokens` setting and try again.");
                }

                return new AiAgentResult
                {
                    Response = assistantMsg.Content ?? "Done.",
                    ToolCallCount = totalToolCalls,
                    WasCancelled = false
                };
            }

            if (progress is not null && !string.IsNullOrWhiteSpace(assistantMsg.Content))
                progress.OnNote(assistantMsg.Content);

            foreach (var toolCall in assistantMsg.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                totalToolCalls++;

                string result;
                JsonElement args = default;
                if (!toolMap.TryGetValue(toolCall.Function.Name, out var tool))
                {
                    result = $"Error: Unknown tool '{toolCall.Function.Name}'";
                }
                else
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(toolCall.Function.Arguments))
                        {
                            using var doc = JsonDocument.Parse(toolCall.Function.Arguments);
                            args = doc.RootElement.Clone();
                        }

                        result = await tool.ExecuteAsync(context, args);
                    }
                    catch (OperationCanceledException)
                    {
                        return new AiAgentResult
                        {
                            Response = "Agent session was cancelled.",
                            ToolCallCount = totalToolCalls,
                            WasCancelled = true
                        };
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error executing AI tool {ToolName}", toolCall.Function.Name);
                        result = $"Error: {ex.Message}";
                    }
                }

                progress?.OnToolCall(toolCall.Function.Name, args, IsErrorResult(result));

                // A tool result can hold text from members or web pages, which must not pose as a <channel_update>.
                messages.Add(new()
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = PromptSanitizer.Sanitize(result)
                });
            }

            if (context.AskPending)
            {
                return new AiAgentResult
                {
                    Response = "",
                    ToolCallCount = totalToolCalls,
                    WasCancelled = false,
                    AskPending = true
                };
            }

            // Goes into the last tool result, because some OpenAI-compatible APIs reject a user message after tool messages.
            if (step + 1 < config.MaxToolCalls && prompt.History?.GetUpdate() is { } update)
            {
                var last = messages[^1];
                messages[^1] = new()
                {
                    Role = last.Role,
                    ToolCallId = last.ToolCallId,
                    Content = last.Content + "\n\n" + update
                };
            }
        }

        return new AiAgentResult
        {
            Response = "Reached the maximum number of steps. The task may be incomplete.",
            ToolCallCount = totalToolCalls,
            WasCancelled = false
        };
    }

    // Adapters return "Error..." text, and generated tool wrappers return {"error":...} JSON.
    private static bool IsErrorResult(string result)
        => result.StartsWith("Error", StringComparison.Ordinal)
           || result.StartsWith("{\"error\"", StringComparison.Ordinal);

    private AiProviderInfo ResolveProviderInternal(AiAgentConfig config)
    {
        var creds = credsProvider.GetCreds();
        return new(
            config.ApiUrl.TrimEnd('/') + "/v1/chat/completions",
            $"Bearer {creds.AiApiKey}");
    }

    private async Task<AgentChatResponse?> CallLlmInternalAsync(
        AiProviderInfo provider,
        AiAgentConfig config,
        AgentChatRequest request,
        CancellationToken ct)
    {
        using var http = httpFactory.CreateClient(ExternalHttp.OWNER_CLIENT_NAME);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", provider.AuthValue);

        if (config.CustomHeaders is { Count: > 0 } headers)
        {
            foreach (var (key, value) in headers)
            {
                if (policy.Managed && IsReservedHeader(key))
                    continue;

                http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
            }
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, provider.Url);
            httpRequest.Content = JsonContent.Create(request);
            using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ExternalHttp.ReadLimitedAsync(response.Content, MAX_ERROR_LOG_BYTES, ct);
                Log.Error(
                    "AI agent API {Url} returned {Status}: {Body}",
                    provider.Url,
                    (int)response.StatusCode,
                    error is { } e ? Encoding.UTF8.GetString(e.Buffer, 0, e.Length) : "<too long>");
                if (error is { } toReturn)
                    ArrayPool<byte>.Shared.Return(toReturn.Buffer);
                return null;
            }

            if (await ExternalHttp.ReadLimitedAsync(response.Content, MAX_REPLY_BYTES, ct) is not { } body)
            {
                Log.Error("AI agent API {Url} sent a reply larger than {Max} bytes", provider.Url, MAX_REPLY_BYTES);
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<AgentChatResponse>(body.Buffer.AsSpan(0, body.Length), _jsonOpts);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(body.Buffer);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to call AI agent API at {Url}", provider.Url);
            return null;
        }
    }
}
