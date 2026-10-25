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
    private const int IDLE_WAIT_MS = 500;
    private const int NO_PROXY_WAIT_MS = 200;
    private const int VOICE_READY_POLL_MS = 20;
    private const int VOICE_READY_TIMEOUT_MS = 10_000;
    private const float UNITY_VOLUME_EPSILON = 0.0001f;

    // With repeat on, a track which cannot play comes back at once, so the player stops instead of retrying it.
    private const int MAX_FAILED_TRACKS_IN_ROW = 3;

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
    private readonly AudioFileCacheConfigService _musicConfig;
    private readonly ISongBuffer _songBuffer;
    private readonly PublicOnlyProxy _publicProxy;
    private readonly Func<IVoiceProxy, SendResult> _sendPcm;
    private readonly Func<IVoiceProxy, SendResult> _sendOpus;

    // Wakes the play loop when a track, a proxy or a skip arrives, instead of waiting for the next poll.
    private readonly SemaphoreSlim _wake = new(0, 1);

    private volatile OpusStreamSource? _opusSource;
    private bool _decodingOpus;
    private byte[]? _opusFrame;
    private byte[]? _decodedPcm;

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
        AudioFileCacheConfigService musicConfig,
        PublicOnlyProxy publicProxy,
        QualityPreset qualityPreset,
        bool autoPlay)
    {
        _publicProxy = publicProxy;
        _musicConfig = musicConfig;
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
        _sendPcm = TryReadAndSendPcm;
        _sendOpus = TryReadAndSendOpus;

        _ = Task.Run(PlayLoopAsync);
    }

    public void SetProxy(IVoiceProxy proxy)
    {
        _proxy = proxy;
        WakeInternal();
    }

    private void WakeInternal()
    {
        if (_wake.CurrentCount > 0)
            return;

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

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
            var failedInRow = 0;
            while (!IsKilled)
            {
                var track = _queue.GetCurrent(out var index);

                if (track is null || IsStopped)
                {
                    failedInRow = 0;
                    await _wake.WaitAsync(IDLE_WAIT_MS);
                    continue;
                }

                if (_proxy is null)
                {
                    await _wake.WaitAsync(NO_PROXY_WAIT_MS);
                    continue;
                }

                if (skipped)
                {
                    skipped = false;
                    _queue.Advance();
                    continue;
                }

                using var cts = new CancellationTokenSource();
                OpusStreamSource? opus = null;
                FfmpegTrackDataSource? ffmpeg = null;
                try
                {
                    if (_proxy is { } p1)
                        _ = p1.StartSpeakingAsync();

                    _ = OnStarted?.Invoke(this, track, index);
                    PrefetchNextInternal();

                    // Voice connects while the stream opens, so the first frame goes out as soon as both are ready.
                    var voiceReady = WaitForVoiceReadyAsync(cts.Token);

                    var source = await GetStreamSourceAsync(track);
                    if (source.Refused)
                    {
                        Log.Warning("Skipped {Track}: its stream is not on a public address", track.Title);
                        StopAfterFailuresInternal(++failedInRow);
                        continue;
                    }

                    if (source.Url is not { } streamUrl)
                    {
                        IsStopped = true;
                        Log.Warning("Stream URL was null for track {Track}", track.Title);
                        continue;
                    }

                    var isLocal = track.Platform == MusicPlatform.Local
                                  || !streamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);

                    string? httpProxy = null;
                    if (!isLocal
                        && source.Service is null
                        && !track.AllowLocalNetwork
                        && !_publicProxy.TryGetUrl(out httpProxy))
                    {
                        Log.Warning("Skipped {Track}: the public-only proxy is not available", track.Title);
                        StopAfterFailuresInternal(++failedInRow);
                        continue;
                    }

                    opus = await TryOpenOpusInternalAsync(track, source.Service, streamUrl, isLocal, cts.Token);

                    Task buffered;
                    if (opus is not null)
                    {
                        Log.Debug("Using Opus passthrough for {Track}", track.Title);
                        buffered = opus.WaitBufferedAsync();
                    }
                    else
                    {
                        _songBuffer.Reset();

                        ffmpeg = FfmpegTrackDataSource.Create(
                            _vc.BitDepth,
                            streamUrl,
                            isLocal,
                            httpProxy,
                            MediaTools.Default.Ffmpeg,
                            source.Service is null ? null : source.Service.FfmpegHeaders ?? string.Empty);

                        if (ffmpeg is null)
                        {
                            IsStopped = true;
                            Log.Error("Please install ffmpeg and make sure it's added to your "
                                      + "PATH environment variable before trying again");
                            continue;
                        }

                        buffered = _songBuffer.BufferAsync(ffmpeg, cts.Token);
                    }

                    if (!await voiceReady)
                    {
                        if (!skipped && !IsStopped && !IsKilled)
                            Log.Warning("Voice not ready after 10s, skipping track");

                        continue;
                    }

                    if (!await WaitWhilePlayingAsync(buffered))
                        continue;

                    if (opus is not null)
                    {
                        _opusSource = opus;
                        _decodingOpus = false;
                    }

                    await RunSendLoopOnThreadAsync(opus is null ? _sendPcm : _sendOpus);
                    failedInRow = 0;
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

                    _opusSource = null;
                    if (opus is not null)
                        await opus.DisposeAsync();

                    ffmpeg?.Dispose();

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
                                    await _trackResolveProvider.QuerySongAsync(related, MusicPlatform.Youtube, false);
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

    private void StopAfterFailuresInternal(int failedInRow)
    {
        if (failedInRow < MAX_FAILED_TRACKS_IN_ROW)
            return;

        IsStopped = true;
        Log.Warning("Stopping the music player: {Count} tracks in a row could not play", failedInRow);
    }

    // A test frame would light the speaking ring before the song has audio, so this only reads the gateway state.
    private async Task<bool> WaitForVoiceReadyAsync(CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(VOICE_READY_POLL_MS));
        do
        {
            if (IsStopped || IsKilled || skipped)
                return false;

            if (_proxy is { IsReady: true })
                return true;

            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= VOICE_READY_TIMEOUT_MS)
                return false;
        } while (await WaitForTickInternalAsync(timer, ct));

        return false;
    }

    // False when a skip or a stop comes before the task ends, so a stalled stream does not hold the queue.
    private async Task<bool> WaitWhilePlayingAsync(Task task)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(VOICE_READY_POLL_MS));
        while (!task.IsCompleted)
        {
            if (IsStopped || IsKilled || skipped)
                return false;

            await timer.WaitForNextTickAsync();
        }

        await task;
        return true;
    }

    private static async ValueTask<bool> WaitForTickInternalAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // yt-dlp and Invidious can return any URL, and ffmpeg reads a value without "http" as a local file.
    public static bool IsAllowedStreamUrl(MusicPlatform platform, bool allowLocalNetwork, string url)
    {
        if (platform == MusicPlatform.Local)
            return allowLocalNetwork;

        if (!allowLocalNetwork)
            return UrlExtensions.IsPublicUrl(url);

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    // The owner set the service url, so its own audio links may point to the local network.
    public static bool IsTrustedServiceStream(MusicPlatform platform, string? serviceBaseUrl, string url)
        => platform == MusicPlatform.Youtube
           && serviceBaseUrl is not null
           && MusicServiceClient.HasSameOrigin(serviceBaseUrl, url);

    // Service is set only for a stream of the music service.
    private readonly record struct StreamSource(string? Url, bool Refused, MusicServiceClient? Service);

    private async Task<StreamSource> GetStreamSourceAsync(IQueuedTrackInfo track)
    {
        if (track.TrackInfo is SimpleTrackInfo sti)
        {
            if (sti.Platform == MusicPlatform.Local
                && sti.Duration == TimeSpan.Zero
                && sti.StreamUrl is not null)
            {
                sti.Duration = await _localTrackResolver.ResolveDurationAsync(sti.StreamUrl);
            }

            if (sti.StreamUrl is not null
                && !IsAllowedStreamUrl(sti.Platform, track.AllowLocalNetwork, sti.StreamUrl))
                return new(null, true, null);

            return new(sti.StreamUrl, false, null);
        }

        var trackId = track.TrackInfo.Id;
        var platform = track.Platform;

        var cachedPath = _audioFileCache.GetCachedPath(trackId, platform);
        if (cachedPath is not null)
            return new(cachedPath, false, null);

        var resolver = _ytResolverFactory.GetYoutubeResolver();
        var url = await resolver.GetStreamUrl(trackId);
        if (url is null)
            return new(null, false, null);

        // The service keeps its own copy, so the bot's file cache does not download it again.
        if (resolver is ServiceYoutubeResolver service
            && IsTrustedServiceStream(platform, service.Service.BaseUrl, url))
            return new(url, false, service.Service);

        if (!IsAllowedStreamUrl(platform, track.AllowLocalNetwork, url))
            return new(null, true, null);

        // Only public streams go to the cache, so a member cannot replay a local stream which the owner queued.
        if (!track.AllowLocalNetwork || UrlExtensions.IsPublicUrl(url))
            _audioFileCache.GetOrStartDownload(trackId, platform, () => Task.FromResult<string?>(url));

        return new(url, false, null);
    }

    private void PrefetchNextInternal()
    {
        if (Repeat == PlayerRepeatType.Track
            || _ytResolverFactory.GetYoutubeResolver() is not ServiceYoutubeResolver service
            || _queue.GetNext(Repeat == PlayerRepeatType.Queue) is not { Platform: MusicPlatform.Youtube } next)
            return;

        _ = service.Service.PrefetchAsync(next.TrackInfo.Id);
    }

    private enum SendResult
    {
        Sent,
        Failed,
        Starved,
        End
    }

    private SendResult TryReadAndSendPcm(IVoiceProxy proxy)
    {
        var data = _songBuffer.Read(_vc.InputLength, out var length);
        if (data.Length == 0)
            return SendResult.End;

        _adjustVolume(data, Volume);
        return proxy.SendPcmFrame(_vc, data, length) ? SendResult.Sent : SendResult.Failed;
    }

    // Sends the packets as they are at full volume. At another volume it decodes, scales and encodes each packet.
    private SendResult TryReadAndSendOpus(IVoiceProxy proxy)
    {
        if (_opusSource is not { } source)
            return SendResult.End;

        var frame = _opusFrame ??= new byte[OpusPacketRing.MAX_PACKET_SIZE];
        switch (source.TryRead(frame, out var length))
        {
            case OpusReadResult.End:
                return SendResult.End;
            case OpusReadResult.Starved:
                return SendResult.Starved;
        }

        var volume = Volume;
        if (Math.Abs(volume - 1f) < UNITY_VOLUME_EPSILON)
        {
            _decodingOpus = false;
            return proxy.SendOpusFrame(_vc, frame, length) ? SendResult.Sent : SendResult.Failed;
        }

        var pcm = _decodedPcm ??= new byte[_vc.InputLength];
        var reset = !_decodingOpus;
        _decodingOpus = true;
        if (_vc.DecodeFrame(frame.AsSpan(0, length), pcm, reset) != _vc.FrameSizePerChannel)
            return proxy.SendOpusFrame(_vc, frame, length) ? SendResult.Sent : SendResult.Failed;

        _adjustVolume(pcm, volume);
        return proxy.SendPcmFrame(_vc, pcm, pcm.Length) ? SendResult.Sent : SendResult.Failed;
    }

    private Task<bool> RunSendLoopOnThreadAsync(Func<IVoiceProxy, SendResult> tryReadAndSend)
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

    private bool SendFrameLoop(Func<IVoiceProxy, SendResult> tryReadAndSend)
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

                if (result == SendResult.End)
                {
                    SendSilenceFrames(5);
                    return true;
                }

                if (result == SendResult.Starved)
                {
                    Thread.Sleep(_vc.Delay);
                    nextFrameTick = Stopwatch.GetTimestamp();
                    continue;
                }

                if (result == SendResult.Sent)
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

    // Only YouTube audio: a cached file or a stream of the music service. Null means ffmpeg plays the track.
    private async Task<OpusStreamSource?> TryOpenOpusInternalAsync(
        IQueuedTrackInfo track,
        MusicServiceClient? service,
        string streamUrl,
        bool isLocal,
        CancellationToken ct)
    {
        if (track.Platform != MusicPlatform.Youtube || (service is null && !isLocal))
            return null;

        Stream? stream;
        if (service is not null)
        {
            stream = await service.OpenAudioAsync(streamUrl, ct);
        }
        else
        {
            try
            {
                stream = WebmOpusDemuxer.OpenFile(streamUrl);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Debug(ex, "Could not open the cached file for Opus passthrough");
                return null;
            }
        }

        if (stream is null)
            return null;

        // A packet must last one voice frame, because the send loop sends one packet per frame.
        return await OpusStreamSource.OpenAsync(stream, _vc.FrameSizePerChannel, ct);
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
        bool allowLocalNetwork,
        MusicPlatform? forcePlatform = null)
    {
        if (IsQueueFull)
            return default;

        var result = await EnqueueInternalAsync(query, queuer, asNext, allowLocalNetwork, forcePlatform);
        if (result.QueuedTrack is { Platform: MusicPlatform.Youtube } queued
            && _ytResolverFactory.GetYoutubeResolver() is ServiceYoutubeResolver service)
            _ = service.Service.PrefetchAsync(queued.TrackInfo.Id);

        return result;
    }

    private async Task<(IQueuedTrackInfo? QueuedTrack, int Index)> EnqueueInternalAsync(
        string query,
        string queuer,
        bool asNext,
        bool allowLocalNetwork,
        MusicPlatform? forcePlatform)
    {
        var song = await _trackResolveProvider.QuerySongAsync(query, forcePlatform, allowLocalNetwork);
        if (song is null || IsQueueFull)
            return default;

        int index;
        var queued = asNext
            ? _queue.EnqueueNext(song, queuer, allowLocalNetwork, out index)
            : _queue.Enqueue(song, queuer, allowLocalNetwork, out index);

        WakeInternal();
        return (queued, index);
    }

    public async Task EnqueueManyAsync(
        IEnumerable<(string Query, MusicPlatform Platform)> queries,
        string queuer,
        bool allowLocalNetwork)
    {
        var errorCount = 0;
        foreach (var chunk in queries.Chunk(5))
        {
            if (IsKilled || IsQueueFull)
                break;

            await chunk.Select(async data =>
                       {
                           var (query, platform) = data;
                           try
                           {
                               if (!IsQueueFull)
                                   await EnqueueInternalAsync(query, queuer, false, allowLocalNetwork, platform);
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

    public int MaxQueueSize
        => _musicConfig.Data.MaxQueueSize;

    public bool IsQueueFull
        => _queue.Count >= MaxQueueSize;

    public void EnqueueTrack(ITrackInfo track, string queuer)
    {
        _queue.Enqueue(track, queuer, out _);
        WakeInternal();
    }

    public void EnqueueTrack(ITrackInfo track, string queuer, bool allowLocalNetwork)
    {
        _queue.Enqueue(track, queuer, allowLocalNetwork, out _);
        WakeInternal();
    }

    public void EnqueueTracks(IEnumerable<ITrackInfo> tracks, string queuer)
    {
        _queue.EnqueueMany(tracks, queuer);
        WakeInternal();
    }

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
        WakeInternal();
    }

    public bool MoveTo(int index)
    {
        if (_queue.SetIndex(index))
        {
            forceIndex = index;
            skipped = true;
            IsStopped = false;
            IsPaused = false;
            WakeInternal();
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

    public IReadOnlyList<(int Index, IQueuedTrackInfo Track)> SearchQueue(string query, int maxResults = 10)
        => _queue.Search(query, maxResults);
}
