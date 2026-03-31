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
    }
}
