using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Utility.AiAgent;

public sealed class AiAgentConfigService : ConfigServiceBase<AiAgentConfig>
{
    private const string FILE_PATH = "data/ai-agent.yml";
    private static readonly TypedKey<AiAgentConfig> _changeKey = new("config.aiagent.updated");

    public override string Name => "aiagent";

    public const int MAX_MODEL_LENGTH = 100;

    private static readonly string[] _reasoningEfforts = ["", "none", "low", "medium", "high", "xhigh"];
    private static readonly int _maxReasoningLength = _reasoningEfforts.Max(static x => x.Length);

    public AiAgentConfigService(IConfigSeria serializer, IPubSub pubSub, BotPolicy policy)
        : base(FILE_PATH, serializer, pubSub, _changeKey, policy)
    {
        AddParsedProp("enabled",
            static c => c.Enabled,
            static (c, v) => c.Enabled = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the AI agent feature is enabled. Default false");

        AddTextProp("model",
            static c => c.ModelName,
            static (c, v) => c.ModelName = v,
            MAX_MODEL_LENGTH,
            "Which model to use for the agent",
            required: true);

        AddNumberProp("maxtools",
            static c => c.MaxToolCalls,
            static (c, v) => c.MaxToolCalls = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of tool calls the agent can make per invocation",
            min: 1,
            max: 50);

        AddNumberProp("maxtokens",
            static c => c.MaxTokens,
            static (c, v) => c.MaxTokens = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Max tokens per response (shared by reasoning + visible output on reasoning models)",
            min: 101,
            max: 128000);

        AddNumberProp("temperature",
            static c => c.Temperature,
            static (c, v) => c.Temperature = v,
            double.TryParse,
            ConfigPrinters.ToString,
            "Temperature for LLM responses",
            min: 0,
            max: 2, strict: true);

        AddNumberProp("memory",
            static c => c.ChannelMessageMemory,
            static (c, v) => c.ChannelMessageMemory = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Number of recent messages per channel the agent remembers",
            min: 0,
            max: 100);

        AddNumberProp("memoryexpiry",
            static c => c.MemoryIdleExpiryMinutes,
            static (c, v) => c.MemoryIdleExpiryMinutes = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Minutes of inactivity before channel memory expires",
            min: 1,
            max: 1440);

        AddTextProp("reasoning",
            static c => c.ReasoningEffort,
            static (c, v) => c.ReasoningEffort = v,
            _maxReasoningLength,
            "Reasoning effort level: none, low, medium, high, xhigh, or empty to disable",
            IsReasoningEffort);

        AddParsedProp("useembed",
            static c => c.UseEmbed,
            static (c, v) => c.UseEmbed = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether AI agent text responses are sent as embeds. Default true");

        AddParsedProp("showactivity",
            static c => c.ShowActivity,
            static (c, v) => c.ShowActivity = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the agent shows a live message with the steps it takes. Default true");

        Migrate();
    }

    private static bool IsReasoningEffort(string value)
        => FindReasoningEffort(value) is not null;

    // The effort names are ASCII.
    private static string? FindReasoningEffort(string value)
    {
        foreach (var effort in _reasoningEfforts)
        {
            if (string.Equals(effort, value, StringComparison.OrdinalIgnoreCase))
                return effort;
        }

        return null;
    }

    // A value which a self-host owner wrote in the file and which is not in the list goes to the provider unchanged.
    public static string? GetReasoningEffort(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : FindReasoningEffort(value) ?? value;

    private void Migrate()
    {
        if (Data.Version < 7)
            MigrateSystemPromptToFileInternal();

        // Versions 2-6 and 8 only removed properties, which the deserializer ignores,
        // and version 9 only added one, which gets its default.
        if (Data.Version < 9)
        {
            ModifyConfig(c =>
            {
                c.Version = 9;
            });
        }
    }

    private void MigrateSystemPromptToFileInternal()
    {
        try
        {
            if (!File.Exists(FILE_PATH))
                return;

            var raw = File.ReadAllText(FILE_PATH);

            // Read before the new schema drops it, so the changes of the operator move to SOUL.md.
            const string key = "SystemPrompt:";
            var idx = raw.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0)
                return;

            // Use YamlDotNet to properly parse the value
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            var dict = deserializer.Deserialize<Dictionary<string, object>>(raw);
            if (dict is null || !dict.TryGetValue("SystemPrompt", out var promptObj))
                return;

            var promptStr = promptObj?.ToString();
            if (string.IsNullOrWhiteSpace(promptStr))
                return;

            // Only write if it differs from the old default (first line check)
            if (promptStr.Contains("You are {botName}, a helpful Discord bot assistant", StringComparison.Ordinal)
                && promptStr.Contains("DISCORD FORMATTING:", StringComparison.Ordinal)
                && promptStr.Contains("COMMAND EXECUTION:", StringComparison.Ordinal))
            {
                // This is the stock default - no need to preserve it, the new defaults cover it
                return;
            }

            var soulPath = Path.Combine("data", "ai", "prompts", "SOUL.md");
            var dir = Path.GetDirectoryName(soulPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            File.WriteAllText(soulPath, promptStr);
            Log.Information("Migrated custom SystemPrompt to {Path}", soulPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to migrate SystemPrompt to SOUL.md");
        }
    }
}
