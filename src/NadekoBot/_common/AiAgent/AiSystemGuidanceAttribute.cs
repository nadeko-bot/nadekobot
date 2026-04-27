namespace NadekoBot.AiAgent;

/// <summary>
/// Per-tool system prompt fragment. The generator emits this as
/// <c>IAiTool.SystemGuidance</c>; SystemPromptBuilder collects, deduplicates,
/// and renders these into the TOOL USAGE slot of the system prompt.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AiSystemGuidanceAttribute(string guidance) : Attribute
{
    public string Guidance { get; } = guidance;
}
