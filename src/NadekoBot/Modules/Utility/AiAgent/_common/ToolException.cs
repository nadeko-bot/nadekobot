namespace NadekoBot.Modules.Utility.AiAgent;

// The generated ExecuteAsync serializes this as {"error":"<code>","message":"<message>"}.
// The code stays short and snake_case, because an LLM keys off it better than off prose.
public sealed class ToolException : Exception
{
    public string Code { get; }

    public ToolException(string code, string message) : base(message)
    {
        Code = code;
    }

    public static ToolException NotFound(string what)
        => new("not_found", what);

    public static ToolException InvalidArgument(string message)
        => new("invalid_argument", message);

    public static ToolException MissingPermission(string permission)
        => new("missing_permission", permission);

    public static ToolException Forbidden(string message)
        => new("forbidden", message);

    // Prefer a specific factory above when the shape of the failure is known.
    public static ToolException Internal(string message)
        => new("internal_error", message);
}
