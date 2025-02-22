#nullable disable
using System.Globalization;

namespace NadekoBot.Common;

public static class BotStringsExtensions
{
    // this one is for pipe fun, see PipeExtensions.cs
    public static string GetText(this IBotStrings strings, in LocStr str, in ulong guildId)
        => strings.GetText(str.Key, guildId, str.Params);
    
    public static string GetText(this IBotStrings strings, in LocStr str, ulong? guildId = null)
        => strings.GetText(str.Key, guildId, str.Params);

    public static string GetText(this IBotStrings strings, in LocStr str, CultureInfo culture)
        => strings.GetText(str.Key, culture, str.Params);
}