namespace NadekoBot.Modules.Music;

public interface ILyricsService
{
    public Task<IReadOnlyList<TracksItem>> SearchTracksAsync(string name);
    public Task<string> GetLyricsAsync(int trackId);
}