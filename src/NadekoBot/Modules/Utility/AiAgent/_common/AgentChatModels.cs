using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class AgentChatRequest
{
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("models")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Models { get; init; }

    [JsonPropertyName("messages")]
    public required List<AgentChatMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<JsonElement>? Tools { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; } = 0.3;

    [JsonPropertyName("max_completion_tokens")]
    public int MaxCompletionTokens { get; init; } = 16384;

    [JsonPropertyName("reasoning_effort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; init; }
}

// The provider's answer also goes into these types, so a field which a provider leaves out must not throw.
public sealed class AgentChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

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

public sealed class AgentToolCall
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentFunctionCall? Function { get; init; }
}

public sealed class AgentFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; init; }
}

public sealed class AgentChatResponse
{
    [JsonPropertyName("choices")]
    public List<AgentChatChoice>? Choices { get; init; }

    [JsonPropertyName("usage")]
    public AgentChatUsage? Usage { get; init; }
}

public sealed class AgentChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public long? PromptTokens { get; init; }

    [JsonPropertyName("prompt_tokens_details")]
    public AgentPromptTokensDetails? PromptTokensDetails { get; init; }
}

public sealed class AgentPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public long? CachedTokens { get; init; }
}

public sealed class AgentChatChoice
{
    [JsonPropertyName("message")]
    public AgentChatMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}
