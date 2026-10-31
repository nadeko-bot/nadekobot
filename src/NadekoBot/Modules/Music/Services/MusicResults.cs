namespace NadekoBot.Modules.Music.Services;

public enum MusicAddStatus
{
    Added,
    NotFound,
    QueueFull,
    JoinFailed
}

// First is the first added track, at FirstIndex in the queue.
public readonly record struct MusicAddResult(
    MusicAddStatus Status,
    int Added = 0,
    IQueuedTrackInfo? First = null,
    int FirstIndex = 0);

// Failed is true when no search source answered, so an empty list means nothing matched.
public readonly record struct MusicSearchResult(IReadOnlyList<ITrackInfo> Tracks, bool Failed);

public readonly record struct SavedPlaylistInfo(int Id, string Name, string Author, int SongCount);
