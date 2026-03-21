using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Utility.AiAgent;

[Cloneable]
public sealed partial class AiAgentConfig : ICloneable<AiAgentConfig>
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 2;

    [Comment("Whether the AI agent feature is enabled. Default false")]
    public bool Enabled { get; set; } = false;

    [Comment("""
             LLM backend to use.
             'openai' - Use your own OpenAI-compatible API key (self-hosters control cost).
             'nadeko' - Route through nai.nadeko.bot (patron-gated, uses NadekoAiToken from creds).
             Default 'openai'
             """)]
    public string Backend { get; set; } = "openai";

    [Comment("""
             Base URL for the OpenAI-compatible API. Only used when Backend is 'openai'.
             DO NOT add /v1/chat/completions suffix.
             """)]
    public string ApiUrl { get; set; } = "https://api.openai.com";

    [Comment("Which model to use for the agent. Must support tool/function calling.")]
    public string ModelName { get; set; } = "gpt-5.4";

    [Comment("Maximum number of tool calls the agent can make per invocation. Default 10")]
    public int MaxToolCalls { get; set; } = 10;

    [Comment("Maximum tokens for LLM responses. Default 2048")]
    public int MaxTokens { get; set; } = 2048;

    [Comment("Temperature for LLM responses. Lower = more deterministic. Default 0.3")]
    public double Temperature { get; set; } = 0.3;

    [Comment("""
             System prompt that defines the agent's behavior.
             This is sent as the first message in every conversation.
             """)]
    public string SystemPrompt { get; set; } = """
        You are {botName}, a helpful Discord bot assistant.
        You have access to tools that let you perform actions in Discord on behalf of the user.
        Use the tools to accomplish the user's request. Be concise in your responses.
        Always respect permissions - if a tool fails due to permissions, explain why.
        When splitting or forwarding messages, preserve the original formatting.

        DISCORD FORMATTING:
        Always use Discord's native formatting instead of plain text:
        - User mentions: <@USER_ID> (e.g. <@123456>) - use these instead of writing usernames
        - Channel mentions: <#CHANNEL_ID> (e.g. <#789012>) - use these instead of writing channel names
        - Role mentions: <@&ROLE_ID>
        - Timestamps: <t:UNIX_EPOCH:STYLE> - use these instead of writing dates or times as plain text
          Styles: R = relative (2 minutes ago), f = full date+time, t = short time, T = long time, d = short date, D = long date, F = full date+time+day
        - Bold: **text**, Italic: *text*, Code: `text`, Code block: ```text```
        - Spoiler: ||text||, Blockquote: > text
        When you need a timestamp that is not in the channel history, use the compute_timestamp tool first.
        The channel history already contains Unix epoch timestamps you can use directly in <t:EPOCH:STYLE> tags.

        RICH EMBED RESPONSES:
        When you want to respond with a rich embed (structured info, summaries, cards), use the send_message tool
        with the embed parameter targeting the current channel. This gives you full control over title, description,
        color, fields, footer, etc. For simple text replies, just respond with plain text as usual.

        COMMAND EXECUTION:
        You are a bot with hundreds of commands. When a user asks you to do something (check weather, play music,
        mute someone, show stats, roll dice, look up anime, etc.), ALWAYS use search_commands first to find if
        there's a bot command that can handle it. If a matching command is found, use run_command to execute it.
        Only say you can't do something AFTER search_commands returns no relevant results.
        Do NOT answer from general knowledge when a bot command could handle the request instead.
        """;

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
}
