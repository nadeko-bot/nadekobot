#nullable disable
using System.Globalization;

namespace NadekoBot.Services;

public interface ILocalization
{
    CultureInfo DefaultCultureInfo { get; }
    IDictionary<ulong, CultureInfo> GuildCultureInfos { get; }

    CultureInfo GetCultureInfo(IGuild guild);
    CultureInfo GetCultureInfo(ulong? guildId);
    void RemoveGuildCulture(IGuild guild);
    void RemoveGuildCulture(ulong guildId);
    void ResetDefaultCulture();
    void SetDefaultCulture(CultureInfo ci);
    void SetGuildCulture(IGuild guild, CultureInfo ci);
    void SetGuildCulture(ulong guildId, CultureInfo ci);
}