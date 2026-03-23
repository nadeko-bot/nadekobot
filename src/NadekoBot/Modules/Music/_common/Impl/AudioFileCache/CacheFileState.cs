namespace NadekoBot.Modules.Music;

public sealed class CacheFileState : IDisposable
{
    private long _bytesWritten;
    private volatile bool _isComplete;
    private volatile bool _isFailed;
    private long _version;
    private readonly ManualResetEventSlim _dataAvailable = new(false);

    public long BytesWritten => Interlocked.Read(ref _bytesWritten);
    public bool IsComplete => _isComplete;
    public bool IsFailed => _isFailed;
    public bool IsDone => _isComplete || _isFailed;

    public long Version => Interlocked.Read(ref _version);

    public void UpdateBytesWritten(long bytes)
    {
        Interlocked.Exchange(ref _bytesWritten, bytes);
        SignalReaders();
    }

    public void MarkComplete()
    {
        _isComplete = true;
        SignalReaders();
    }

    public void MarkFailed()
    {
        _isFailed = true;
        SignalReaders();
    }

    public bool WaitForData(long knownVersion, TimeSpan timeout)
    {
        if (Interlocked.Read(ref _version) != knownVersion)
            return true;

        _dataAvailable.Reset();

        // double-check after reset to close the race window
        if (Interlocked.Read(ref _version) != knownVersion)
            return true;

        return _dataAvailable.Wait(timeout);
    }

    private void SignalReaders()
    {
        Interlocked.Increment(ref _version);
        _dataAvailable.Set();
    }

    public void Dispose()
        => _dataAvailable.Dispose();
}
