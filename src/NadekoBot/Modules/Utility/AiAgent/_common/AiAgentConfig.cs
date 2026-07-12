using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class AiAgentConfig
{
    [Comment("DO NOT CHANGE THE VERSION MANUALLY")]
    public int Version { get; set; } = 8;

    [Comment("Whether the AI agent feature is enabled. Default false")]
    public bool Enabled { get; set; } = false;

    [Comment("""
             Base URL for the OpenAI-compatible API.
             DO NOT add /v1/chat/completions suffix.
             """)]
    public string ApiUrl { get; set; } = "https://api.openai.com";

    [Comment("Which model to use for the agent. Must support tool/function calling.")]
    public string ModelName { get; set; } = "gpt-5.4";

    [Comment("""
             Optional list of model IDs for providers that support fallback routing (e.g. OpenRouter).
             If set, this is sent as "models" instead of "model" in the request body.
             Example: ["openai/gpt-5.4", "anthropic/claude-opus-4", "google/gemini-3.1-pro"]
             """)]
    public List<string> Models { get; set; } = [];

    [Comment("Maximum number of tool calls the agent can make per invocation. Default 10")]
    public int MaxToolCalls { get; set; } = 10;

    [Comment("""
             Max tokens per response. For reasoning models this budget is shared by
             reasoning tokens AND the visible reply, so keep it generous or replies
             may be truncated. Default 16384
             """)]
    public int MaxTokens { get; set; } = 16384;

    [Comment("Temperature for LLM responses. Lower = more deterministic. Default 0.3")]
    public double Temperature { get; set; } = 0.3;

    [Comment("""
             List of allowed tool names. If empty, all tools are available.
             Example: ["send_message", "get_message"]
             """)]
    public List<string> AllowedTools { get; set; } = [];

    [Comment("Number of recent messages per channel the agent remembers. 0 to disable. Default 20")]
    public int ChannelMessageMemory { get; set; } = 20;

    [Comment("Minutes of inactivity before channel memory expires and stops observing. Default 30")]
    public int MemoryIdleExpiryMinutes { get; set; } = 30;

    [Comment("Enable triggering the agent by saying the bot's name (with intent classification). Default true")]
    public bool NameTriggerEnabled { get; set; } = true;

    [Comment("Seconds after an agent response during which the user's messages go directly to the agent. Default 120")]
    public int FollowUpWindowSeconds { get; set; } = 120;

    [Comment("""
             Custom HTTP headers sent with every LLM request.
             Useful for provider-specific headers like X-OpenRouter-Title.
             Example: { "X-OpenRouter-Title": "Nadeko" }
             """)]
    public Dictionary<string, string> CustomHeaders { get; set; } = new()
    {
        ["X-OpenRouter-Title"] = "Nadeko"
    };

    [Comment("""
             Reasoning effort level for models that support it (e.g. GPT-5.x, Claude, o-series).
             Values: "none", "minimal", "low", "medium", "high", "xhigh", "max". Empty string to disable.
             Lower values save output tokens on simple tool-calling tasks. Default "low"
             """)]
    public string ReasoningEffort { get; set; } = "low";

    [Comment("Whether AI agent text responses are sent as embeds. If false, sent as plain text. Default true")]
    public bool UseEmbed { get; set; } = true;
}
