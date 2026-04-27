namespace NadekoBot.AiAgent;

/// <summary>
/// Marker for adapters that produce CORE AI tools (always loaded, exposed in
/// every chat completion request) rather than data tools (semantically searched
/// and invoked via invoke_data_tool).
///
/// Generator behaviour for IAiCoreToolGroup:
///  - tool name regex `^(get|list|find|count|describe|check)_...` is NOT enforced
///  - generated IAiTool emits IsDataTool => false
///  - Task&lt;string&gt; return values are passed back to the LLM verbatim
///    (instead of being JSON-serialized)
/// </summary>
public interface IAiCoreToolGroup : IAiToolGroup;
