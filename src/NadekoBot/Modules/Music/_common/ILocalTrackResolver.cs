#nullable disable
namespace NadekoBot.Modules.Music;

public interface ILocalTrackResolver : IPlatformQueryResolver
{
    IAsyncEnumerable<ITrackInfo> ResolveDirectoryAsync(string dirPath);
}