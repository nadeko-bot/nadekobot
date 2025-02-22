namespace NadekoBot.Modules.Music;

public sealed partial class MusicQueue
{
    private sealed class QueuedTrackInfo : IQueuedTrackInfo
    {
        public ITrackInfo TrackInfo { get; }
        public string Queuer { get; }

        public string Title
            => TrackInfo.Title;

        public string Url
            => TrackInfo.Url;

        public string Thumbnail
            => TrackInfo.Thumbnail;

        public TimeSpan Duration
            => TrackInfo.Duration;

        public MusicPlatform Platform
            => TrackInfo.Platform;


        public QueuedTrackInfo(ITrackInfo trackInfo, string queuer)
        {
            TrackInfo = trackInfo;
            Queuer = queuer;
        }
    }
}

public sealed partial class MusicQueue : IMusicQueue
{
    public int Index
    {
        get
        {
            // just make sure the internal logic runs first
            // to make sure that some potential intermediate value is not returned
            lock (_locker)
            {
                return index;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_locker)
            {
                return tracks.Count;
            }
        }
    }

    private LinkedList<QueuedTrackInfo> tracks;

    private int index;
    private int? _lastQueued = null;

    private readonly object _locker = new();

    public MusicQueue()
    {
        index = 0;
        tracks = new();
    }

    public IQueuedTrackInfo Enqueue(ITrackInfo trackInfo, string queuer, out int enqueuedAt)
    {
        lock (_locker)
        {
            var added = new QueuedTrackInfo(trackInfo, queuer);
            _lastQueued = enqueuedAt = tracks.Count;
            tracks.AddLast(added);
          
            return added;
        }
    }


    public IQueuedTrackInfo EnqueueNext(ITrackInfo trackInfo, string queuer, out int trackIndex)
    {
        lock (_locker)
        {
            if (tracks.Count == 0)
                return Enqueue(trackInfo, queuer, out trackIndex);

            var currentNode = tracks.First!;
            int i;
            for (i = 1; i <= index; i++)
                currentNode = currentNode.Next!; // can't be null because index is always in range of the count

            var added = new QueuedTrackInfo(trackInfo, queuer);
            trackIndex = i;

            tracks.AddAfter(currentNode, added);

            _lastQueued = i;

            return added;
        }
    }

    public void EnqueueMany(IEnumerable<ITrackInfo> toEnqueue, string queuer)
    {
        lock (_locker)
        {
            foreach (var track in toEnqueue)
            {
                var added = new QueuedTrackInfo(track, queuer);
                tracks.AddLast(added);
            }
            
            _lastQueued = tracks.Count;
        }
    }

    public IReadOnlyCollection<IQueuedTrackInfo> List()
    {
        lock (_locker)
        {
            return tracks.ToList();
        }
    }

    public IQueuedTrackInfo? GetCurrent(out int currentIndex)
    {
        lock (_locker)
        {
            currentIndex = index;
            return tracks.ElementAtOrDefault(index);
        }
    }

    public void Advance()
    {
        lock (_locker)
        {
            if (++index >= tracks.Count)
                index = 0;
        }
    }

    public void Clear()
    {
        lock (_locker)
        {
            tracks.Clear();
            _lastQueued = null;
        }
    }

    public bool SetIndex(int newIndex)
    {
        lock (_locker)
        {
            if (newIndex < 0 || newIndex >= tracks.Count)
                return false;

            index = newIndex;
            return true;
        }
    }

    private void RemoveAtInternal(int remoteAtIndex, out IQueuedTrackInfo trackInfo)
    {
        var removedNode = tracks.First!;
        int i;
        for (i = 0; i < remoteAtIndex; i++)
            removedNode = removedNode.Next!;

        trackInfo = removedNode.Value;
        tracks.Remove(removedNode);

        if (i <= index)
            --index;

        if (index < 0)
            index = Count;

        if (i < _lastQueued)
        {
            if (_lastQueued is not null)
            {
                _lastQueued -= 1;
            }
        }
        else if (i == _lastQueued)
        {
            _lastQueued = null;
        }
        
        // if it was the last song in the queue
        // // wrap back to start
        // if (_index == Count)
        //     _index = 0;
        // else if (i <= _index)
        //     if (_index == 0)
        //         _index = Count;
        //     else --_index;
    }

    public void RemoveCurrent()
    {
        lock (_locker)
        {
            if (index < tracks.Count)
                RemoveAtInternal(index, out _);
        }
    }

    public IQueuedTrackInfo? MoveTrack(int from, int to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfEqual(to, from);

        lock (_locker)
        {
            if (from >= Count || to >= Count)
                return null;

            if (from == _lastQueued)
                _lastQueued = to;
            else if (to == _lastQueued)
                _lastQueued += 1;

            // update current track index
            if (from == index)
            {
                // if the song being moved is the current track
                // it means that it will for sure end up on the destination
                index = to;
            }
            else
            {
                // moving a track from below the current track means 
                // means it will drop down
                if (from < index)
                    index--;

                // moving a track to below the current track
                // means it will rise up
                if (to <= index)
                    index++;


                // if both from and to are below _index - net change is + 1 - 1 = 0
                // if from is below and to is above - net change is -1 (as the track is taken and put above)
                // if from is above and to is below - net change is 1 (as the track is inserted under)
                // if from is above and to is above - net change is 0
            }

            // get the node which needs to be moved
            var fromNode = tracks.First!;
            for (var i = 0; i < from; i++)
                fromNode = fromNode.Next!;

            // remove it from the queue
            tracks.Remove(fromNode);

            // if it needs to be added as a first node,
            // add it directly and return
            if (to == 0)
            {
                tracks.AddFirst(fromNode);
                return fromNode.Value;
            }

            // else find the node at the index before the specified target
            var addAfterNode = tracks.First!;
            for (var i = 1; i < to; i++)
                addAfterNode = addAfterNode.Next!;

            // and add after it
            tracks.AddAfter(addAfterNode, fromNode);
            return fromNode.Value;
        }
    }

    public void Shuffle(Random rng)
    {
        lock (_locker)
        {
            var list = tracks.ToArray();
            rng.Shuffle(list);
            tracks = new(list);
            _lastQueued = null;
        }
    }

    public bool IsLast()
    {
        lock (_locker)
        {
            return index == tracks.Count // if there are no tracks
                   || index == tracks.Count - 1;
        }
    }

    public void ReorderFairly()
    {
        lock (_locker)
        {
            var groups = new Dictionary<string, int>();
            var queuers = new List<Queue<QueuedTrackInfo>>();

            foreach (var track in tracks.Skip(index).Concat(tracks.Take(index)))
            {
                if (!groups.TryGetValue(track.Queuer, out var qIndex))
                {
                    queuers.Add(new Queue<QueuedTrackInfo>());
                    qIndex = queuers.Count - 1;
                    groups.Add(track.Queuer, qIndex);
                }

                queuers[qIndex].Enqueue(track);
            }

            tracks = new LinkedList<QueuedTrackInfo>();
            index = 0;

            while (true)
            {
                for (var i = 0; i < queuers.Count; i++)
                {
                    var queue = queuers[i];
                    tracks.AddLast(queue.Dequeue());

                    if (queue.Count == 0)
                    {
                        queuers.RemoveAt(i);
                        i--;
                    }
                }

                if (queuers.Count == 0)
                    break;
            }

            _lastQueued = null;
        }
    }

    public bool TryRemoveAt(int remoteAt, out IQueuedTrackInfo? trackInfo, out bool isCurrent)
    {
        lock (_locker)
        {
            isCurrent = false;
            trackInfo = null;

            if (remoteAt < 0 || remoteAt >= tracks.Count)
                return false;

            if (remoteAt == index)
                isCurrent = true;

            RemoveAtInternal(remoteAt, out trackInfo);

            return true;
        }
    }
    
    public int? GetLastQueuedIndex() => _lastQueued;
}