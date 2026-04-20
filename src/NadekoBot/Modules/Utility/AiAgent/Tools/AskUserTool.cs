using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

public sealed class AskUserTool(IMessageSenderService sender) : IAiTool, INService
{
    public string Name => "ask_user";

    public string Description =>
        "Ask the invoking user a clarifying question before proceeding. " +
        "The current session will pause after the question is sent. " +
        "When the user replies, a new session starts with the answer visible in channel history. " +
        "Use this when the user's request is ambiguous and you need more information to proceed correctly. " +
        "You can optionally provide multiple-choice options.";

    public string? SystemGuidance => """
        ASKING FOR CLARIFICATION:
        When the user's request is ambiguous, use the ask_user tool to ask a clarifying question before proceeding.
        Limit questions to 2-3 per session unless absolutely necessary.
        If you can make a reasonable assumption, prefer acting over asking.
        """;

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "question": {
                    "type": "string",
                    "description": "The question to ask the user"
                },
                "options": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Optional list of choices. Displayed as a numbered list. The user can reply with a number or free-form text."
                }
            },
            "required": ["question"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("question", out var questionEl))
            return "Error: question is required.";

        var question = questionEl.GetString();
        if (string.IsNullOrWhiteSpace(question))
            return "Error: question cannot be empty.";

        var sb = new StringBuilder(question);

        if (arguments.TryGetProperty("options", out var optionsEl)
            && optionsEl.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine();
            var i = 1;
            foreach (var opt in optionsEl.EnumerateArray())
            {
                var optText = opt.GetString();
                if (!string.IsNullOrWhiteSpace(optText))
                {
                    sb.AppendLine();
                    sb.Append($"**{i}.** {optText}");
                    i++;
                }
            }
        }

        var eb = sender.CreateEmbed(context.Guild.Id)
            .WithPendingColor()
            .WithDescription(sb.ToString().TrimTo(4096));

        await context.SourceChannel.SendMessageAsync(embed: eb.Build());

        context.AskPending = true;

        return "Question sent. The session will now end. The user's reply will be available in channel history on the next invocation.";
    }
}
