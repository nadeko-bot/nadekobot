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
    private readonly IVoiceProxy _proxy;
    private readonly IGoogleApiService _googleApiService;
    private readonly ISongBuffer _songBuffer;

    private bool skipped;
    private int? forceIndex;
    private readonly Thread _thread;
    private readonly Random _rng;

    public bool AutoPlay { get; set; }

    public MusicPlayer(
        IMusicQueue queue,
        ITrackResolveProvider trackResolveProvider,
        IYoutubeResolverFactory ytResolverFactory,
        IVoiceProxy proxy,
        IGoogleApiService googleApiService,
        QualityPreset qualityPreset,
        bool autoPlay)
    {
        _queue = queue;
        _trackResolveProvider = trackResolveProvider;
        _ytResolverFactory = ytResolverFactory;
        _proxy = proxy;
        _googleApiService = googleApiService;
        AutoPlay = autoPlay;
        _rng = new NadekoRandom();

        _vc = GetVoiceClient(qualityPreset);
        if (_vc.BitDepth == 16)
            _adjustVolume = AdjustVolumeInt16;
        else
            _adjustVolume = AdjustVolumeFloat32;

        _songBuffer = new PoopyBufferImmortalized(_vc.InputLength);

        _thread = new(async () =>
        {
            try
            {
                await PlayLoop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Music player thread crashed");
            }
        });
        _thread.Start();
    }

    private static VoiceClient GetVoiceClient(QualityPreset qualityPreset)
        => qualityPreset switch
        {
            QualityPreset.Highest => new(),
            QualityPreset.High => new(SampleRate._48k, Bitrate._128k, Channels.Two, FrameDelay.Delay40),
            QualityPreset.Medium => new(SampleRate._48k,
                Bitrate._96k,
                Channels.Two,
                FrameDelay.Delay40,
                BitDepthEnum.UInt16),
            QualityPreset.Low => new(SampleRate._48k,
                Bitrate._64k,
                Channels.Two,
                FrameDelay.Delay40,
                BitDepthEnum.UInt16),
            _ => throw new ArgumentOutOfRangeException(nameof(qualityPreset), qualityPreset, null)
        };

    private async Task PlayLoop()
    {
        var sw = new Stopwatch();

        while (!IsKilled)
        {
            // wait until a song is available in the queue
            // or until the queue is resumed
            var track = _queue.GetCurrent(out var index);

            if (track is null || IsStopped)
            {
                await Task.Delay(500);
                continue;
            }

            if (skipped)
            {
                skipped = false;
                _queue.Advance();
                continue;
            }

            using var cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;
            try
            {
                // light up green in vc
                _ = _proxy.StartSpeakingAsync();

                _ = OnStarted?.Invoke(this, track, index);

                // make sure song buffer is ready to be (re)used
                _songBuffer.Reset();

                var streamUrl = await GetStreamUrl(track);
                // start up the data source
                using var source = FfmpegTrackDataSource.CreateAsync(
                    _vc.BitDepth,
                    streamUrl,
                    track.Platform == MusicPlatform.Local);

                // start moving data from the source into the buffer
                // this method will return once the sufficient prebuffering is done
                await _songBuffer.BufferAsync(source, token);

                // // Implemenation with multimedia timer. Works but a hassle because no support for switching
                // // vcs, as any error in copying will cancel the song. Also no idea how to use this as an option
                // // for selfhosters.
                // if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                // {
                //     var cancelSource = new CancellationTokenSource();
                //     var cancelToken = cancelSource.Token;
                //     using var timer = new MultimediaTimer(_ =>
                //     {
                //         if (IsStopped || IsKilled)
                //         {
                //             cancelSource.Cancel();
                //             return;
                //         }
                //         
                //         if (_skipped)
                //         {
                //             _skipped = false;
                //             cancelSource.Cancel();
                //             return;
                //         }
                //
                //         if (IsPaused)
                //             return;
                //
                //         try
                //         {
                //             // this should tolerate certain number of errors
                //             var result = CopyChunkToOutput(_songBuffer, _vc);
                //             if (!result)
                //                 cancelSource.Cancel();
                //               
                //         }
                //         catch (Exception ex)
                //         {
                //             Log.Warning(ex, "Something went wrong sending voice data: {ErrorMessage}", ex.Message);
                //             cancelSource.Cancel();
                //         }
                //
                //     }, null, 20);
                //     
                //     while(true)
                //         await Task.Delay(1000, cancelToken);
                // }

                // start sending data
                var ticksPerMs = 1000f / Stopwatch.Frequency;
                sw.Start();
                Thread.Sleep(2);

                var delay = sw.ElapsedTicks * ticksPerMs > 3f ? _vc.Delay - 16 : _vc.Delay - 3;

                var errorCount = 0;
                while (!IsStopped && !IsKilled)
                {
                    // doing the skip this way instead of in the condition
                    // ensures that a song will for sure be skipped
                    if (skipped)
                    {
                        skipped = false;
                        break;
                    }

                    if (IsPaused)
                    {
                        await Task.Delay(200);
                        continue;
                    }

                    sw.Restart();
                    var ticks = sw.ElapsedTicks;
                    try
                    {
                        var result = CopyChunkToOutput(_songBuffer, _vc);

                        // if song is finished
                        if (result is null)
                            break;

                        if (result is true)
                        {
                            if (errorCount > 0)
                            {
                                _ = _proxy.StartSpeakingAsync();
                                errorCount = 0;
                            }

                            // FUTURE windows multimedia api

                            // wait for slightly less than the latency
                            Thread.Sleep(delay);

                            // and then spin out the rest
                            while ((sw.ElapsedTicks - ticks) * ticksPerMs <= _vc.Delay - 0.1f)
                                Thread.SpinWait(100);
                        }
                        else
                        {
                            // result is false is either when the gateway is being swapped 
                            // or if the bot is reconnecting, or just disconnected for whatever reason

                            // tolerate up to 15x200ms of failures (3 seconds)
                            if (++errorCount <= 15)
                            {
                                await Task.Delay(200);
                                continue;
                            }

                            Log.Warning("Can't send data to voice channel");

                            IsStopped = true;
                            // if errors are happening for more than 3 seconds
                            // Stop the player
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Something went wrong sending voice data: {ErrorMessage}", ex.Message);
                    }
                }
            }
            catch (Win32Exception)
            {
                IsStopped = true;
                Log.Error("Please install ffmpeg and make sure it's added to your "
                          + "PATH environment variable before trying again");
            }
            catch (OperationCanceledException)
            {
                Log.Information("Song skipped");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unknown error in music loop: {ErrorMessage}", ex.Message);
                await Task.Delay(3_000);
            }
            finally
            {
                cancellationTokenSource.Cancel();
                // turn off green in vc

                _ = OnCompleted?.Invoke(this, track);

                if (AutoPlay && track.Platform == MusicPlatform.Youtube)
                {
                    try
                    {
                        var relatedSongs = await _googleApiService.GetRelatedVideosAsync(track.TrackInfo.Id, 5);
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

                _ = _proxy.StopSpeakingAsync();

                await Task.Delay(100);
            }
        }
    }

    private async Task<string?> GetStreamUrl(IQueuedTrackInfo track)
    {
        if (track.TrackInfo is SimpleTrackInfo sti)
            return sti.StreamUrl;

        return await _ytResolverFactory.GetYoutubeResolver().GetStreamUrl(track.TrackInfo.Id);
    }

    private bool? CopyChunkToOutput(ISongBuffer sb, VoiceClient vc)
    {
        var data = sb.Read(vc.InputLength, out var length);

        // if nothing is read from the buffer, song is finished
        if (data.Length == 0)
            return null;

        _adjustVolume(data, Volume);
        return _proxy.SendPcmFrame(vc, data, length);
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

        // if queue is being repeated, advance no matter what
        if (repeat == PlayerRepeatType.None)
        {
            // if this is the last song,
            // stop the queue
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


        var wasLast = _queue.IsLast();

        try
        {
            int index;
            if (asNext)
                return (_queue.EnqueueNext(song, queuer, out index), index);

            return (_queue.Enqueue(song, queuer, out index), index);
        }
        finally
        {
            // if (wasLast && IsStopped)
            // {
            //     IsStopped = false;
            // }
        }
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

            // > 10 errors in a row = kill
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
        => _queue.Shuffle(_rng);

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