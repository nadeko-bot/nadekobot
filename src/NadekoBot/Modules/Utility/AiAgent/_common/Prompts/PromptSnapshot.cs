namespace NadekoBot.Modules.Utility.AiAgent.Prompts;

public sealed class PromptSnapshot
{
    public static readonly PromptSnapshot Empty = new(string.Empty, string.Empty);

    public string Soul { get; }
    public string Operator { get; }

    public PromptSnapshot(string soul, string operatorDoc)
    {
        Soul = soul;
        Operator = operatorDoc;
    }
}
