#nullable disable
namespace NadekoBot.Services;

/// <summary>
///     Implemented by classes which provide localized strings in their own ways
/// </summary>
public interface IBotStringsProvider
{
    /// <summary>
    ///     Gets localized string
    /// </summary>
    /// <param name="localeName">Language name</param>
    /// <param name="key">String key</param>
    /// <returns>Localized string</returns>
    string GetText(string localeName, string key);

    /// <summary>
    ///     Reloads string cache
    /// </summary>
    void Reload();

    /// <summary>
    ///     Gets command arg examples and description
    /// </summary>
    /// <param name="localeName">Language name</param>
    /// <param name="commandName">Command name</param>
    CommandStrings GetCommandStrings(string localeName, string commandName);
}