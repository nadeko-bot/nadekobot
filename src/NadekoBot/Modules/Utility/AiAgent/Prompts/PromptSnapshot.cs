using System.Collections.Frozen;

namespace NadekoBot.Modules.Utility.AiAgent.Prompts;

public sealed class PromptSnapshot
{
    public static readonly PromptSnapshot Empty = new(
        string.Empty,
        string.Empty,
        FrozenDictionary<string, string>.Empty);

    public string Soul { get; }
    public string Operator { get; }
    public FrozenDictionary<string, string> Modules { get; }

    public PromptSnapshot(
        string soul,
        string operatorDoc,
        FrozenDictionary<string, string> modules)
    {
        Soul = soul;
        Operator = operatorDoc;
        Modules = modules;
    }
}
