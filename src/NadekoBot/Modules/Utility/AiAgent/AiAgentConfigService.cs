using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Config service for the AI agent feature
/// </summary>
public sealed class AiAgentConfigService : ConfigServiceBase<AiAgentConfig>
{
    private const string FILE_PATH = "data/ai-agent.yml";
    private static readonly TypedKey<AiAgentConfig> _changeKey = new("config.aiagent.updated");

    public override string Name => "aiagent";

    public AiAgentConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("enabled",
            static c => c.Enabled,
            static (c, v) => c.Enabled = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether the AI agent feature is enabled. Default false");

        AddParsedProp("model",
            static c => c.ModelName,
            static (c, v) => c.ModelName = v,
            ConfigParsers.String,
            ConfigPrinters.ToString,
            "Which model to use for the agent");

        AddParsedProp("maxtools",
            static c => c.MaxToolCalls,
            static (c, v) => c.MaxToolCalls = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum number of tool calls the agent can make per invocation",
            static val => val is > 0 and <= 50);

        AddParsedProp("maxtokens",
            static c => c.MaxTokens,
            static (c, v) => c.MaxTokens = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Maximum tokens for LLM responses",
            static val => val is > 100 and <= 16384);

        AddParsedProp("temperature",
            static c => c.Temperature,
            static (c, v) => c.Temperature = v,
            double.TryParse,
            ConfigPrinters.ToString,
            "Temperature for LLM responses",
            static val => val is >= 0 and <= 2);

        AddParsedProp("memory",
            static c => c.ChannelMessageMemory,
            static (c, v) => c.ChannelMessageMemory = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Number of recent messages per channel the agent remembers",
            static val => val is >= 0 and <= 100);

        AddParsedProp("memoryexpiry",
            static c => c.MemoryIdleExpiryMinutes,
            static (c, v) => c.MemoryIdleExpiryMinutes = v,
            int.TryParse,
            ConfigPrinters.ToString,
            "Minutes of inactivity before channel memory expires",
            static val => val is >= 1 and <= 1440);

        AddParsedProp("reasoning",
            static c => c.ReasoningEffort,
            static (c, v) => c.ReasoningEffort = v,
            ConfigParsers.String,
            ConfigPrinters.ToString,
            "Reasoning effort level: none, low, medium, high, xhigh, or empty to disable");

        AddParsedProp("useembed",
            static c => c.UseEmbed,
            static (c, v) => c.UseEmbed = v,
            bool.TryParse,
            ConfigPrinters.ToString,
            "Whether AI agent text responses are sent as embeds. Default true");

        Migrate();
    }

    private void Migrate()
    {
        if (Data.Version < 2)
        {
            ModifyConfig(c =>
            {
                c.Version = 2;
            });
        }

        if (Data.Version < 3)
        {
            ModifyConfig(c =>
            {
                c.Version = 3;
            });
        }

        if (Data.Version < 4)
        {
            ModifyConfig(c =>
            {
                c.Version = 4;
            });
        }

        if (Data.Version < 5)
        {
            ModifyConfig(c =>
            {
                c.Version = 5;
            });
        }

        if (Data.Version < 6)
        {
            ModifyConfig(c =>
            {
                c.Version = 6;
            });
        }

        if (Data.Version < 7)
        {
            MigrateSystemPromptToFileInternal();
            ModifyConfig(c =>
            {
                c.Version = 7;
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

            // Extract SystemPrompt from the raw YAML before it's dropped by the new schema.
            // The property was a multiline YAML string. If present, write it to SOUL.md
            // so operator customizations are preserved.
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
