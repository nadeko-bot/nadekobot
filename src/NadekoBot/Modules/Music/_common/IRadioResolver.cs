#nullable disable
namespace NadekoBot.Modules.Music;

public interface IRadioResolver : IPlatformQueryResolver
{
    Task<ITrackInfo> ResolveByQueryAsync(string query, bool allowLocalNetwork);
}