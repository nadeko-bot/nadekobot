#nullable disable
using NadekoBot.Common.Configs;
using NadekoBot.Modules.Games.Common;

namespace NadekoBot.Modules.Games.Services;

public sealed class GamesConfigService : ConfigServiceBase<GamesConfig>
{
    private const string FILE_PATH = "data/games.yml";
    private static readonly TypedKey<GamesConfig> _changeKey = new("config.games.updated");
    public override string Name { get; } = "games";

    public GamesConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("trivia.min_win_req",
            gs => gs.Trivia.MinimumWinReq,
            int.TryParse,
            ConfigPrinters.ToString,
            val => val > 0);
        AddParsedProp("trivia.currency_reward",
            gs => gs.Trivia.CurrencyReward,
            long.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);
        AddParsedProp("hangman.currency_reward",
            gs => gs.Hangman.CurrencyReward,
            long.TryParse,
            ConfigPrinters.ToString,
            val => val >= 0);
        AddParsedProp("chatbot",
            gs => gs.ChatBot,
            ConfigParsers.InsensitiveEnum,
            ConfigPrinters.ToString);

        AddParsedProp("gpt.apiUrl",
            gs => gs.ChatGpt.ApiUrl,
            ConfigParsers.String,
            ConfigPrinters.ToString);

        AddParsedProp("gpt.modelName",
            gs => gs.ChatGpt.ModelName,
            ConfigParsers.String,
            ConfigPrinters.ToString);

        AddParsedProp("gpt.personality",
            gs => gs.ChatGpt.PersonalityPrompt,
            ConfigParsers.String,
            ConfigPrinters.ToString);

        Migrate();
    }

    private void Migrate()
    {
        if (data.Version < 1)
        {
            ModifyConfig(c =>
            {
                c.Version = 1;
                c.Hangman = new()
                {
                    CurrencyReward = 0
                };
            });
        }

        if (data.Version < 3)
        {
            ModifyConfig(c =>
            {
                c.Version = 3;
                c.ChatGpt.ModelName = "gpt35turbo";
            });
        }

        if (data.Version < 4)
        {
            ModifyConfig(c =>
            {
                c.Version = 4;
#pragma warning disable CS0612 // Type or member is obsolete
                c.ChatGpt.ModelName =
                    c.ChatGpt.ModelName.Equals("gpt4", StringComparison.OrdinalIgnoreCase)
                    || c.ChatGpt.ModelName.Equals("gpt432k", StringComparison.OrdinalIgnoreCase)
                        ? "gpt-4o"
                        : "gpt-3.5-turbo";
#pragma warning restore CS0612 // Type or member is obsolete
            });
        }

        if (data.Version < 5)
        {
            ModifyConfig(c =>
            {
                c.Version = 5;
                c.ChatBot = c.ChatBot == ChatBotImplementation.OpenAi
                    ? ChatBotImplementation.OpenAi
                    : c.ChatBot;

                if (c.ChatGpt.ModelName.Equals("gpt4o", StringComparison.OrdinalIgnoreCase))
                {
                    c.ChatGpt.ModelName = "gpt-4o";
                }
                else if (c.ChatGpt.ModelName.Equals("gpt35turbo", StringComparison.OrdinalIgnoreCase))
                {
                    c.ChatGpt.ModelName = "gpt-3.5-turbo";
                }
                else
                {
                    Log.Warning(
                        "Unknown OpenAI api model name: {ModelName}. "
                        + "It will be reset to 'gpt-3.5-turbo' only this time",
                        c.ChatGpt.ModelName);
                    c.ChatGpt.ModelName = "gpt-3.5-turbo";
                }
            });
        }
    }
}