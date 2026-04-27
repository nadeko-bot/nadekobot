using System.Net.Http.Json;
using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

public readonly record struct AiProviderInfo(string Url, string AuthValue);

public sealed class AiAgentSession(
    IHttpClientFactory httpFactory,
    IBotCredsProvider credsProvider) : IAiAgentSession, INService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonElement _ephemeralCacheControl =
        JsonDocument.Parse("""{"type":"ephemeral"}""").RootElement.Clone();

    public async Task<OneOf<AiAgentResult, Error<string>>> RunAsync(
        string userPrompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        string systemPrompt,
        Func<string?>? channelHistoryProvider,
        CancellationToken ct = default)
    {
        var provider = ResolveProviderInternal(config);

        var reasoning = !string.IsNullOrWhiteSpace(config.ReasoningEffort)
            ? new AgentReasoningConfig { Effort = config.ReasoningEffort, Exclude = true }
            : null;

        var toolMap = tools.ToDictionary(t => t.Name);
        var messages = new List<AgentChatMessage>();

        messages.Add(new()
        {
            Role = "system",
            Content = systemPrompt
        });

        var historyIndex = -1;
        if (channelHistoryProvider is not null)
        {
            var initialHistory = channelHistoryProvider();
            if (initialHistory is not null)
            {
                historyIndex = messages.Count;
                messages.Add(new()
                {
                    Role = "user",
                    Content = initialHistory
                });
            }
        }

        messages.Add(new()
        {
            Role = "user",
            Content = userPrompt
        });

        var totalToolCalls = 0;

        for (var step = 0; step < config.MaxToolCalls; step++)
        {
            ct.ThrowIfCancellationRequested();

            // Refresh channel history slot before every request after the first so the
            // model sees output the bot just posted (e.g. a command's embed) on its way
            // back into the loop.
            if (step > 0 && historyIndex >= 0 && channelHistoryProvider is not null)
            {
                var refreshed = channelHistoryProvider();
                if (refreshed is not null)
                {
                    messages[historyIndex] = new()
                    {
                        Role = "user",
                        Content = refreshed
                    };
                }
            }

            var request = new AgentChatRequest
            {
                Model = config.Models is { Count: > 0 } ? null : config.ModelName,
                Models = config.Models is { Count: > 0 } ? config.Models : null,
                Messages = messages,
                Tools = toolSchemas.Count > 0 ? toolSchemas.ToList() : null,
                MaxTokens = config.MaxTokens,
                Temperature = config.Temperature,
                CacheControl = _ephemeralCacheControl,
                Reasoning = reasoning
            };

            var response = await CallLlmInternalAsync(provider, config, request, ct);
            if (response is null)
                return new Error<string>("Failed to get response from AI provider.");

            var choice = response.Choices?.FirstOrDefault();
            if (choice?.Message is null)
                return new Error<string>("Received empty response from AI provider.");

            var assistantMsg = choice.Message;
            messages.Add(assistantMsg);

            if (assistantMsg.ToolCalls is not { Count: > 0 })
            {
                return new AiAgentResult
                {
                    Response = assistantMsg.Content ?? "Done.",
                    ToolCallCount = totalToolCalls,
                    WasCancelled = false
                };
            }

            foreach (var toolCall in assistantMsg.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                totalToolCalls++;

                string result;
                if (!toolMap.TryGetValue(toolCall.Function.Name, out var tool))
                {
                    result = $"Error: Unknown tool '{toolCall.Function.Name}'";
                }
                else
                {
                    try
                    {
                        JsonElement args;
                        if (string.IsNullOrWhiteSpace(toolCall.Function.Arguments))
                        {
                            args = default;
                        }
                        else
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

                messages.Add(new()
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = result
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
        }

        return new AiAgentResult
        {
            Response = "Reached the maximum number of steps. The task may be incomplete.",
            ToolCallCount = totalToolCalls,
            WasCancelled = false
        };
    }

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
        using var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", provider.AuthValue);

        if (config.CustomHeaders is { Count: > 0 } headers)
        {
            foreach (var (key, value) in headers)
                http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        try
        {
            using var response = await http.PostAsJsonAsync(provider.Url, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                Log.Error(
                    "AI agent API {Url} returned {Status}: {Body}",
                    provider.Url,
                    (int)response.StatusCode,
                    body);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<AgentChatResponse>(_jsonOpts, ct);
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
