namespace NadekoBot.Medusa;

/// <summary>
/// Overridden to implement parsers for custom types 
/// </summary>
/// <typeparam name="T">Type into which to parse the input</typeparam>
public abstract class ParamParser<T>
{
    /// <summary>
    /// Overridden to implement parsing logic
    /// </summary>
    /// <param name="ctx">Context</param>
    /// <param name="input">Input to parse</param>
    /// <returns>A <see cref="ParseResult{T}"/> with successful or failed status</returns>
    public abstract ValueTask<ParseResult<T>> TryParseAsync(AnyContext ctx, string input);
}