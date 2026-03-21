using System.Net.Http.Json;
using System.Text.Json;
using OneOf;
using OneOf.Types;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Executes the ReAct agent loop: prompt -> LLM -> tool calls -> results -> LLM -> repeat.
/// Supports both self-hosted OpenAI-compatible APIs and the nadeko backend.
/// </summary>
public sealed class AiAgentSession(
    IHttpClientFactory httpFactory,
    IBotCredsProvider credsProvider) : IAiAgentSession, INService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Run the agent loop until the LLM produces a final text response or the step limit is hit
    /// </summary>
    public async Task<OneOf<AiAgentResult, Error<string>>> RunAsync(
        string userPrompt,
        AiToolContext context,
        IReadOnlyList<IAiTool> tools,
        IReadOnlyList<JsonElement> toolSchemas,
        AiAgentConfig config,
        string systemPrompt,
        string? channelHistory,
        CancellationToken ct = default)
    {
        var toolMap = tools.ToDictionary(t => t.Name);
        var messages = new List<AgentChatMessage>();

        messages.Add(new()
        {
            Role = "system",
            Content = systemPrompt
        });

        if (channelHistory is not null)
        {
            messages.Add(new()
            {
                Role = "user",
                Content = channelHistory
            });
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

            var request = new AgentChatRequest
            {
                Model = config.ModelName,
                Messages = messages,
                Tools = toolSchemas.Count > 0 ? toolSchemas.ToList() : null,
                MaxTokens = config.MaxTokens,
                Temperature = config.Temperature
            };

            var response = await CallLlmAsync(config, request, ct);
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
        }

        return new AiAgentResult
        {
            Response = "Reached the maximum number of steps. The task may be incomplete.",
            ToolCallCount = totalToolCalls,
            WasCancelled = false
        };
    }

    private async Task<AgentChatResponse?> CallLlmAsync(
        AiAgentConfig config,
        AgentChatRequest request,
        CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        var creds = credsProvider.GetCreds();

        string url;
        if (config.Backend == "nadeko")
        {
            url = "https://nai.nadeko.bot/v1/chat/completions";
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-auth-token", creds.NadekoAiToken);
        }
        else
        {
            url = config.ApiUrl.TrimEnd('/') + "/v1/chat/completions";
            http.DefaultRequestHeaders.Authorization = new("Bearer", creds.AiApiKey);
        }

        try
        {
            using var response = await http.PostAsJsonAsync(url, request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AgentChatResponse>(_jsonOpts, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to call AI agent API at {Url}", url);
            return null;
        }
    }
}
