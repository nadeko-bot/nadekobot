#nullable disable

namespace NadekoBot.Services;

/// <summary>
///     Basic interface used for classes implementing strings loading mechanism
/// </summary>
public interface IStringsSource
{
    /// <summary>
    ///     Gets all response strings
    /// </summary>
    /// <returns>Dictionary(localename, Dictionary(key, response))</returns>
    Dictionary<string, Dictionary<string, string>> GetResponseStrings();

    Dictionary<string, Dictionary<string, CommandStrings>> GetCommandStrings();
}