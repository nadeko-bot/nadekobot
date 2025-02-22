#nullable disable
using System.Globalization;

namespace NadekoBot.Common;

/// <summary>
///     Defines methods to retrieve and reload bot strings
/// </summary>
public interface IBotStrings
{
    string GetText(string key, ulong? guildId = null, params object[] data);
    string GetText(string key, CultureInfo locale, params object[] data);
    void Reload();
    CommandStrings GetCommandStrings(string commandName, ulong? guildId = null);
    CommandStrings GetCommandStrings(string commandName, CultureInfo cultureInfo);
}