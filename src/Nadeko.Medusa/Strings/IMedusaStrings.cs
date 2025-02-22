using System.Globalization;

namespace NadekoBot.Medusa;

/// <summary>
///     Defines methods to retrieve and reload medusa strings
/// </summary>
public interface IMedusaStrings
{
    // string GetText(string key, ulong? guildId = null, params object[] data);
    string? GetText(string key, CultureInfo locale, params object[] data);
    void Reload();
    CommandStrings GetCommandStrings(string commandName, CultureInfo cultureInfo);
    string? GetDescription(CultureInfo? locale);
}