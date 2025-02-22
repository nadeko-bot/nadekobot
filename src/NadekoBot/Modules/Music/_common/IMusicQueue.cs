namespace NadekoBot.Modules.Music;

public interface IMusicQueue
{
    int Index { get; }
    int Count { get; }
    IQueuedTrackInfo Enqueue(ITrackInfo trackInfo, string queuer, out int index);
    IQueuedTrackInfo EnqueueNext(ITrackInfo song, string queuer, out int index);

    void EnqueueMany(IEnumerable<ITrackInfo> tracks, string queuer);

    public IReadOnlyCollection<IQueuedTrackInfo> List();
    IQueuedTrackInfo? GetCurrent(out int index);
    void Advance();
    void Clear();
    bool SetIndex(int index);
    bool TryRemoveAt(int index, out IQueuedTrackInfo? trackInfo, out bool isCurrent);
    void RemoveCurrent();
    IQueuedTrackInfo? MoveTrack(int from, int to);
    void Shuffle(Random rng);
    bool IsLast();
    void ReorderFairly();
    int? GetLastQueuedIndex();
}