namespace NadekoBot.Modules.Utility.AiAgent;

/// <summary>
/// Thrown by adapter methods to signal a structured tool error to the LLM.
/// The generator-emitted ExecuteAsync wraps this and serialises a stable
/// JSON shape: <c>{"error":"&lt;code&gt;","message":"&lt;message&gt;"}</c>.
///
/// Use a short snake_case <see cref="Code"/> -- LLMs key off it more reliably
/// than free-form prose. Reserved codes used across the codebase:
/// <c>missing_permission</c>, <c>not_found</c>, <c>invalid_argument</c>,
/// <c>forbidden</c>.
/// </summary>
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

    /// <summary>
    /// Catch-all for unexpected adapter failures bubbled up to the LLM.
    /// Prefer the specific factories above whenever the failure shape is known.
    /// </summary>
    public static ToolException Internal(string message)
        => new("internal_error", message);
}
