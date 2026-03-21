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
            c => c.Enabled,
            bool.TryParse,
            ConfigPrinters.ToString);

        AddParsedProp("backend",
            c => c.Backend,
            ConfigParsers.String,
            ConfigPrinters.ToString,
            val => val is "openai" or "nadeko");

        AddParsedProp("model",
            c => c.ModelName,
            ConfigParsers.String,
            ConfigPrinters.ToString);

        AddParsedProp("maxtools",
            c => c.MaxToolCalls,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val is > 0 and <= 50);

        AddParsedProp("maxtokens",
            c => c.MaxTokens,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val is > 100 and <= 16384);

        AddParsedProp("temperature",
            c => c.Temperature,
            double.TryParse,
            ConfigPrinters.ToString,
            val => val is >= 0 and <= 2);

        AddParsedProp("memory",
            c => c.ChannelMessageMemory,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val is >= 0 and <= 100);

        AddParsedProp("memoryexpiry",
            c => c.MemoryIdleExpiryMinutes,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val is >= 1 and <= 1440);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 2)
        {
            ModifyConfig(c =>
            {
                c.Version = 2;
            });
        }
    }
}
