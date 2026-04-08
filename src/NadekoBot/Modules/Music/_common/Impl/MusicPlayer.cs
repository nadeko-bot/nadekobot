using NadekoBot.Voice;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music.Resolvers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NadekoBot.Modules.Music;

public sealed class MusicPlayer : IMusicPlayer
{
    private const int MAX_SEND_ERRORS = 50;
    private const int ERROR_SLEEP_MS = 200;

    public event Func<IMusicPlayer, IQueuedTrackInfo, Task>? OnCompleted;
    public event Func<IMusicPlayer, IQueuedTrackInfo, int, Task>? OnStarted;
    public event Func<IMusicPlayer, Task>? OnQueueStopped;
    public bool IsKilled { get; private set; }
    public bool IsStopped { get; private set; }
    public bool IsPaused { get; private set; }
    public PlayerRepeatType Repeat { get; private set; }

    public int CurrentIndex
        => _queue.Index;

    public float Volume { get; private set; } = 1.0f;

    private readonly AdjustVolumeDelegate _adjustVolume;
    private readonly VoiceClient _vc;

    private readonly IMusicQueue _queue;
    private readonly ITrackResolveProvider _trackResolveProvider;
    private readonly IYoutubeResolverFactory _ytResolverFactory;
    private readonly ILocalTrackResolver _localTrackResolver;
    private volatile IVoiceProxy? _proxy;
    private readonly IGoogleApiService _googleApiService;
    private readonly AudioFileCacheService _audioFileCache;
    private readonly ISongBuffer _songBuffer;

    private volatile bool skipped;
    private int? forceIndex;

    public bool AutoPlay { get; set; }

    public MusicPlayer(
        IMusicQueue queue,
        ITrackResolveProvider trackResolveProvider,
        IYoutubeResolverFactory ytResolverFactory,
        ILocalTrackResolver localTrackResolver,
        IVoiceProxy? proxy,
        IGoogleApiService googleApiService,
        AudioFileCacheService audioFileCache,
        QualityPreset qualityPreset,
        bool autoPlay)
    {
        _queue = queue;
        _trackResolveProvider = trackResolveProvider;
        _ytResolverFactory = ytResolverFactory;
        _localTrackResolver = localTrackResolver;
        _proxy = proxy;
        _googleApiService = googleApiService;
        _audioFileCache = audioFileCache;
        AutoPlay = autoPlay;

        _vc = GetVoiceClient(qualityPreset);
        if (_vc.BitDepth == 16)
            _adjustVolume = AdjustVolumeInt16;
        else
            _adjustVolume = AdjustVolumeFloat32;

        _songBuffer = new PoopyBufferImmortalized(_vc.InputLength);

        _ = Task.Run(PlayLoopAsync);
    }

    public void SetProxy(IVoiceProxy proxy)
        => _proxy = proxy;

    private static VoiceClient GetVoiceClient(QualityPreset qualityPreset)
        => qualityPreset switch
        {
            QualityPreset.Highest or QualityPreset.High => new(),
            QualityPreset.Medium or QualityPreset.Low => new(SampleRate._48k,
                Bitrate._96k,
                Channels.Two,
                FrameDelay.Delay40,
                BitDepthEnum.UInt16),
            _ => throw new ArgumentOutOfRangeException(nameof(qualityPreset), qualityPreset, null)
        };

    private async Task PlayLoopAsync()
    {
        try
        {
            while (!IsKilled)
            {
                var track = _queue.GetCurrent(out var index);

                if (track is null || IsStopped)
                {
                    await Task.Delay(500);
                    continue;
                }

                if (_proxy is null)
                {
                    await Task.Delay(200);
                    continue;
                }

                if (skipped)
                {
                    skipped = false;
                    _queue.Advance();
                    continue;
                }

                using var cts = new CancellationTokenSource();
                try
                {
                    if (_proxy is { } p1)
                        _ = p1.StartSpeakingAsync();

                    _ = OnStarted?.Invoke(this, track, index);

                    var streamUrl = await GetStreamSourceAsync(track);

                    var isLocal = track.Platform == MusicPlatform.Local
                                  || (streamUrl is not null
                                      && !streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase));

                    if (!await WaitForVoiceReadyAsync())
                    {
                        Log.Warning("Voice not ready after 10s, skipping track");
                        continue;
                    }

                    var songFinished = false;

                    // passthrough only for cached local files at unity volume
                    if (isLocal && track.Platform == MusicPlatform.Youtube
                        && Math.Abs(Volume - 1f) < 0.0001f
                        && streamUrl is not null)
                    {
                        songFinished = await TryPlayOpusPassthroughAsync(streamUrl);
                    }

                    if (!songFinished)
                    {
                        _songBuffer.Reset();

                        using var source = FfmpegTrackDataSource.CreateAsync(
                            _vc.BitDepth,
                            streamUrl,
                            isLocal);

                        if (source is null)
                        {
                            IsStopped = true;
                            Log.Error("Please install ffmpeg and make sure it's added to your "
                                      + "PATH environment variable before trying again");
                            continue;
                        }

                        await _songBuffer.BufferAsync(source, cts.Token);
                        songFinished = await RunSendLoopOnThreadAsync(TryReadAndSendPcm);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Song cancelled");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in play loop: {ErrorMessage}", ex.Message);
                    await Task.Delay(3_000);
                }
                finally
                {
                    await cts.CancelAsync();

                    _ = OnCompleted?.Invoke(this, track);

                    if (AutoPlay && track.Platform == MusicPlatform.Youtube)
                    {
                        try
                        {
                            var relatedSongs =
                                await _googleApiService.GetRelatedVideosAsync(track.TrackInfo.Id, 5);
                            var related = relatedSongs.Shuffle().FirstOrDefault();
                            if (related is not null)
                            {
                                var relatedTrack =
                                    await _trackResolveProvider.QuerySongAsync(related, MusicPlatform.Youtube);
                                if (relatedTrack is not null)
                                    EnqueueTrack(relatedTrack, "Autoplay");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed queueing a related song via autoplay");
                        }
                    }

                    HandleQueuePostTrack();
                    skipped = false;

                    if (_proxy is { } p3)
                        _ = p3.StopSpeakingAsync();

                    await Task.Delay(100);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PlayLoop crashed");
        }
    }

    private async Task<bool> WaitForVoiceReadyAsync()
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 10_000)
        {
            if (IsStopped || IsKilled || skipped)
                return false;

            var proxy = _proxy;
            if (proxy is not null && proxy.SendOpusFrame(_vc, OpusSilenceFrame, OpusSilenceFrame.Length))
                return true;

            await Task.Delay(ERROR_SLEEP_MS);
        }

        return false;
    }

    private async Task<string?> GetStreamSourceAsync(IQueuedTrackInfo track)
    {
        if (track.TrackInfo is SimpleTrackInfo sti)
        {
            if (sti.Platform == MusicPlatform.Local
                && sti.Duration == TimeSpan.Zero
                && sti.StreamUrl is not null)
            {
                sti.Duration = await _localTrackResolver.ResolveDurationAsync(sti.StreamUrl);
            }

            return sti.StreamUrl;
        }

        var trackId = track.TrackInfo.Id;
        var platform = track.Platform;

        var cachedPath = _audioFileCache.GetCachedPath(trackId, platform);
        if (cachedPath is not null)
            return cachedPath;

        var url = await _ytResolverFactory.GetYoutubeResolver().GetStreamUrl(trackId);
        _audioFileCache.GetOrStartDownload(trackId, platform, () => Task.FromResult(url));
        return url;
    }

    private bool? TryReadAndSendPcm(IVoiceProxy proxy)
    {
        var data = _songBuffer.Read(_vc.InputLength, out var length);
        if (data.Length == 0)
            return null;

        _adjustVolume(data, Volume);
        return proxy.SendPcmFrame(_vc, data, length);
    }

    private Task<bool> RunSendLoopOnThreadAsync(Func<IVoiceProxy, bool?> tryReadAndSend)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                var result = SendFrameLoop(tryReadAndSend);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Send thread error");
                tcs.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
            Name = "MusicSend"
        };

        thread.Start();
        return tcs.Task;
    }

    private bool SendFrameLoop(Func<IVoiceProxy, bool?> tryReadAndSend)
    {
        using var sleeper = PrecisionSleeper.Create();
        var ticksPerFrame = (long)(Stopwatch.Frequency * _vc.Delay / 1000.0);
        var nextFrameTick = Stopwatch.GetTimestamp();
        var errorCount = 0;

        while (!IsStopped && !IsKilled)
        {
            if (skipped)
            {
                skipped = false;
                return false;
            }

            if (IsPaused)
            {
                Thread.Sleep(ERROR_SLEEP_MS);
                nextFrameTick = Stopwatch.GetTimestamp();
                continue;
            }

            var proxy = _proxy;
            if (proxy is null)
            {
                IsStopped = true;
                return false;
            }

            try
            {
                var result = tryReadAndSend(proxy);

                if (result is null)
                {
                    SendSilenceFrames(5);
                    return true;
                }

                if (result is true)
                {
                    if (errorCount > 0)
                    {
                        _ = proxy.StartSpeakingAsync();
                        errorCount = 0;
                    }

                    nextFrameTick += ticksPerFrame;
                    sleeper.SleepUntil(nextFrameTick);
                }
                else
                {
                    if (++errorCount <= MAX_SEND_ERRORS)
                    {
                        if (errorCount % 10 == 0)
                            Log.Debug("Voice send errors: {ErrorCount}/{Max}", errorCount, MAX_SEND_ERRORS);
                        Thread.Sleep(ERROR_SLEEP_MS);
                        nextFrameTick = Stopwatch.GetTimestamp();
                        continue;
                    }

                    Log.Warning("Can't send after {ErrorCount} consecutive failures", errorCount);
                    IsStopped = true;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Send frame error");
                nextFrameTick = Stopwatch.GetTimestamp();
            }
        }

        return false;
    }

    private async Task<bool> TryPlayOpusPassthroughAsync(string filePath)
    {
        try
        {
            using var demuxer = new WebmOpusDemuxer(filePath);
            if (!demuxer.Initialize() || !demuxer.IsOpus)
                return false;

            Log.Debug("Using Opus passthrough for {FilePath}", filePath);

            bool? TryReadAndSendOpus(IVoiceProxy proxy)
            {
                if (!demuxer.TryReadPacket(out var opusData, out var opusLength))
                    return null;

                return proxy.SendOpusFrame(_vc, opusData, opusLength);
            }

            return await RunSendLoopOnThreadAsync(TryReadAndSendOpus);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Opus passthrough failed, falling back to ffmpeg");
            return false;
        }
    }

    private static readonly byte[] OpusSilenceFrame = [0xF8, 0xFF, 0xFE];

    private void SendSilenceFrames(int count)
    {
        var proxy = _proxy;
        if (proxy is null) return;

        for (var i = 0; i < count; i++)
        {
            try
            {
                proxy.SendOpusFrame(_vc, OpusSilenceFrame, OpusSilenceFrame.Length);
                Thread.Sleep(_vc.Delay);
            }
            catch
            {
                break;
            }
        }
    }

    private void HandleQueuePostTrack()
    {
        if (forceIndex is { } index)
        {
            _queue.SetIndex(index);
            forceIndex = null;
            return;
        }

        var (repeat, isStopped) = (Repeat, IsStopped);

        if (repeat == PlayerRepeatType.Track || isStopped)
            return;

        if (repeat == PlayerRepeatType.None)
        {
            if (_queue.IsLast())
            {
                IsStopped = true;
                OnQueueStopped?.Invoke(this);
                return;
            }

            _queue.Advance();
            return;
        }

        _queue.Advance();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AdjustVolumeInt16(Span<byte> audioSamples, float volume)
    {
        if (Math.Abs(volume - 1f) < 0.0001f)
            return;

        var samples = MemoryMarshal.Cast<byte, short>(audioSamples);

        for (var i = 0; i < samples.Length; i++)
        {
            ref var sample = ref samples[i];
            sample = (short)(sample * volume);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AdjustVolumeFloat32(Span<byte> audioSamples, float volume)
    {
        if (Math.Abs(volume - 1f) < 0.0001f)
            return;

        var samples = MemoryMarshal.Cast<byte, float>(audioSamples);

        for (var i = 0; i < samples.Length; i++)
        {
            ref var sample = ref samples[i];
            sample *= volume;
        }
    }

    public async Task<(IQueuedTrackInfo? QueuedTrack, int Index)> TryEnqueueTrackAsync(
        string query,
        string queuer,
        bool asNext,
        MusicPlatform? forcePlatform = null)
    {
        var song = await _trackResolveProvider.QuerySongAsync(query, forcePlatform);
        if (song is null)
            return default;

        int index;
        if (asNext)
            return (_queue.EnqueueNext(song, queuer, out index), index);

        return (_queue.Enqueue(song, queuer, out index), index);
    }

    public async Task EnqueueManyAsync(IEnumerable<(string Query, MusicPlatform Platform)> queries, string queuer)
    {
        var errorCount = 0;
        foreach (var chunk in queries.Chunk(5))
        {
            if (IsKilled)
                break;

            await chunk.Select(async data =>
                       {
                           var (query, platform) = data;
                           try
                           {
                               await TryEnqueueTrackAsync(query, queuer, false, platform);
                               errorCount = 0;
                           }
                           catch (Exception ex)
                           {
                               Log.Warning(ex, "Error resolving {MusicPlatform} Track {TrackQuery}", platform, query);
                               ++errorCount;
                           }
                       })
                       .WhenAll();

            await Task.Delay(1000);

            if (errorCount > 10)
                break;
        }
    }

    public void EnqueueTrack(ITrackInfo track, string queuer)
        => _queue.Enqueue(track, queuer, out _);

    public void EnqueueTracks(IEnumerable<ITrackInfo> tracks, string queuer)
        => _queue.EnqueueMany(tracks, queuer);

    public void SetRepeat(PlayerRepeatType type)
        => Repeat = type;

    public void ShuffleQueue()
        => _queue.Shuffle();

    public void Stop()
        => IsStopped = true;

    public void Clear()
    {
        _queue.Clear();
        skipped = true;
    }

    public IReadOnlyCollection<IQueuedTrackInfo> GetQueuedTracks()
        => _queue.List();

    public IQueuedTrackInfo? GetCurrentTrack(out int index)
        => _queue.GetCurrent(out index);

    public void Next()
    {
        skipped = true;
        IsStopped = false;
        IsPaused = false;
    }

    public bool MoveTo(int index)
    {
        if (_queue.SetIndex(index))
        {
            forceIndex = index;
            skipped = true;
            IsStopped = false;
            IsPaused = false;
            return true;
        }

        return false;
    }

    public void SetVolume(int newVolume)
    {
        var normalizedVolume = newVolume / 100f;
        if (normalizedVolume is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(newVolume), "Volume must be in range 0-100");

        Volume = normalizedVolume;
    }

    public void Kill()
    {
        IsKilled = true;
        IsStopped = true;
        IsPaused = false;
        skipped = true;
    }

    public bool TryRemoveTrackAt(int index, out IQueuedTrackInfo? trackInfo)
    {
        if (!_queue.TryRemoveAt(index, out trackInfo, out var isCurrent))
            return false;

        if (isCurrent)
            skipped = true;

        return true;
    }

    public bool TogglePause()
        => IsPaused = !IsPaused;

    public IQueuedTrackInfo? MoveTrack(int from, int to)
        => _queue.MoveTrack(from, to);

    public void Dispose()
    {
        IsKilled = true;
        OnCompleted = null;
        OnStarted = null;
        OnQueueStopped = null;
        _queue.Clear();
        _songBuffer.Dispose();
        _vc.Dispose();
    }

    private delegate void AdjustVolumeDelegate(Span<byte> data, float volume);

    public void SetFairplay()
    {
        _queue.ReorderFairly();
    }

    public Task<IQueuedTrackInfo?> RemoveLastQueuedTrack()
    {
        var last = _queue.GetLastQueuedIndex();
        if (last is null)
            return Task.FromResult<IQueuedTrackInfo?>(null);

        return TryRemoveTrackAt(last.Value, out var trackInfo)
            ? Task.FromResult(trackInfo)
            : Task.FromResult<IQueuedTrackInfo?>(null);
    }
}
