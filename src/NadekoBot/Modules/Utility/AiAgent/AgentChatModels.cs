using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// OpenAI chat completion request with tool support
/// </summary>
public sealed class AgentChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required List<AgentChatMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<JsonElement>? Tools { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.3;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; } = 2048;
}

/// <summary>
/// A message in the agent conversation (supports system, user, assistant, and tool roles)
/// </summary>
public sealed class AgentChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }
}

/// <summary>
/// A tool call requested by the LLM
/// </summary>
public sealed class AgentToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required AgentFunctionCall Function { get; init; }
}

/// <summary>
/// The function name and arguments from a tool call
/// </summary>
public sealed class AgentFunctionCall
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public required string Arguments { get; init; }
}

/// <summary>
/// OpenAI chat completion response
/// </summary>
public sealed class AgentChatResponse
{
    [JsonPropertyName("choices")]
    public List<AgentChatChoice>? Choices { get; init; }

    [JsonPropertyName("usage")]
    public AgentUsage? Usage { get; init; }
}

/// <summary>
/// A single choice in the response
/// </summary>
public sealed class AgentChatChoice
{
    [JsonPropertyName("message")]
    public AgentChatMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

/// <summary>
/// Token usage info
/// </summary>
public sealed class AgentUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}
