namespace NadekoBot.Modules.Music;

public interface IPlatformQueryResolver
{
    Task<ITrackInfo?> ResolveByQueryAsync(string query);
}