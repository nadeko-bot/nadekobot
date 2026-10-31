using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Music.Resolvers;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Music.Services;

public sealed class MusicService : IMusicService, IPlaceholderProvider
{
    private const int SEARCH_RESULTS = 5;
    public const int MAX_PLAYLISTS_PER_USER = 25;

    private readonly AyuVoiceStateService _voiceStateService;
    private readonly ITrackResolveProvider _trackResolveProvider;
    private readonly DbService _db;
    private readonly IYoutubeResolverFactory _ytResolver;
    private readonly ILocalTrackResolver _localResolver;
    private readonly DiscordSocketClient _client;
    private readonly IBotStrings _strings;
    private readonly IGoogleApiService _googleApiService;
    private readonly AudioFileCacheService _audioFileCache;
    private readonly AudioFileCacheConfigService _musicConfig;
    private readonly YtLoader _ytLoader;
    private readonly IMessageSenderService _sender;
    private readonly PublicOnlyProxy _publicProxy;

    private readonly ConcurrentDictionary<ulong, IMusicPlayer> _players;
    private readonly ConcurrentDictionary<ulong, (ITextChannel Default, ITextChannel? Override)> _outputChannels;
    private readonly ConcurrentDictionary<ulong, MusicPlayerSettings> _settings;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _voiceLocks = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _createLocks = new();
    private readonly SemaphoreSlim _playlistLoadLock = new(1, 1);

    public MusicService(
        AyuVoiceStateService voiceStateService,
        ITrackResolveProvider trackResolveProvider,
        DbService db,
        IYoutubeResolverFactory ytResolver,
        ILocalTrackResolver localResolver,
        DiscordSocketClient client,
        IBotStrings strings,
        IGoogleApiService googleApiService,
        AudioFileCacheService audioFileCache,
        AudioFileCacheConfigService musicConfig,
        YtLoader ytLoader,
        IMessageSenderService sender,
        PublicOnlyProxy publicProxy)
    {
        _publicProxy = publicProxy;
        _voiceStateService = voiceStateService;
        _trackResolveProvider = trackResolveProvider;
        _db = db;
        _ytResolver = ytResolver;
        _localResolver = localResolver;
        _client = client;
        _strings = strings;
        _googleApiService = googleApiService;
        _audioFileCache = audioFileCache;
        _musicConfig = musicConfig;
        _ytLoader = ytLoader;
        _sender = sender;

        _players = new();
        _outputChannels = new ConcurrentDictionary<ulong, (ITextChannel, ITextChannel?)>();
        _settings = new();

        _client.LeftGuild += ClientOnLeftGuild;
    }

    private void DisposeMusicPlayer(IMusicPlayer musicPlayer)
    {
        musicPlayer.Kill();
        _ = DisposeLaterInternalAsync(musicPlayer);
    }

    private static async Task DisposeLaterInternalAsync(IMusicPlayer musicPlayer)
    {
        await Task.Delay(10_000);
        musicPlayer.Dispose();
    }

    private void RemoveMusicPlayer(ulong guildId)
    {
        _outputChannels.TryRemove(guildId, out _);
        if (_players.TryRemove(guildId, out var mp))
            DisposeMusicPlayer(mp);
    }

    private Task ClientOnLeftGuild(SocketGuild guild)
    {
        RemoveMusicPlayer(guild.Id);
        return Task.CompletedTask;
    }

    public async Task LeaveVoiceChannelAsync(ulong guildId)
    {
        RemoveMusicPlayer(guildId);
        await _voiceStateService.LeaveVoiceChannel(guildId);
    }

    public Task JoinVoiceChannelAsync(ulong guildId, ulong voiceChannelId)
        => _voiceStateService.JoinVoiceChannel(guildId, voiceChannelId);

    // The lock covers the check too, so two callers of this method cannot both join.
    public Task<bool> EnsureVoiceChannelAsync(ulong guildId, ulong voiceChannelId)
        => JoinLockedInternalAsync(guildId, voiceChannelId, onlyWhenNeeded: true);

    private async Task<bool> JoinLockedInternalAsync(ulong guildId, ulong voiceChannelId, bool onlyWhenNeeded)
    {
        var voiceLock = _voiceLocks.GetOrAdd(guildId, static _ => new SemaphoreSlim(1, 1));
        await voiceLock.WaitAsync();
        try
        {
            if (onlyWhenNeeded
                && _client.GetGuild(guildId)?.CurrentUser?.VoiceChannel is not null
                && _players.ContainsKey(guildId))
                return true;

            return await _voiceStateService.JoinVoiceChannel(guildId, voiceChannelId) is not null;
        }
        finally
        {
            voiceLock.Release();
        }
    }

    // A new player starts a play loop and rents a large buffer, so a server gets one only when it has none.
    public async Task<IMusicPlayer> GetOrCreateMusicPlayerAsync(ITextChannel defaultChannel, bool followChannel = true)
    {
        var guildId = defaultChannel.GuildId;
        var (player, created) = await OncePerKey.GetOrCreateAsync(_players,
            _createLocks,
            guildId,
            (Service: this, Channel: defaultChannel),
            static s => s.Service.CreateMusicPlayerInternalAsync(s.Channel.GuildId, s.Channel));

        // The player's messages follow the last music command, and a deleted music channel falls back to it.
        if (!created && followChannel)
            _outputChannels[guildId] = (defaultChannel, GetSavedMusicChannelInternal(guildId));

        return player;
    }

    private ITextChannel? GetSavedMusicChannelInternal(ulong guildId)
        => _settings.TryGetValue(guildId, out var settings) && settings.MusicChannelId is { } channelId
            ? _client.GetGuild(guildId)?.GetTextChannel(channelId)
            : null;

    public bool TryGetMusicPlayer(ulong guildId, [MaybeNullWhen(false)] out IMusicPlayer musicPlayer)
        => _players.TryGetValue(guildId, out musicPlayer);

    /// <summary>
    /// Attaches the voice proxy to the music player for the given guild.
    /// Call after voice connection is established.
    /// </summary>
    public void AttachProxy(ulong guildId)
    {
        if (_players.TryGetValue(guildId, out var mp)
            && _voiceStateService.TryGetProxy(guildId, out var proxy))
        {
            mp.SetProxy(proxy);
        }
    }

    public async Task<MusicAddResult> EnqueueYoutubePlaylistAsync(IMusicPlayer mp, string query, string queuer)
    {
        if (mp.IsQueueFull)
            return new(MusicAddStatus.QueueFull);

        var count = 0;
        IQueuedTrackInfo? first = null;
        var firstIndex = 0;
        try
        {
            await foreach (var track in _ytResolver.GetYoutubeResolver().ResolveTracksFromPlaylistAsync(query))
            {
                if (mp.IsKilled)
                    break;

                var (queued, index) = mp.EnqueueResolvedTrack(track, queuer, false, false);
                if (queued is null)
                    break;

                if (count++ == 0)
                    (first, firstIndex) = (queued, index);
            }
        }
        catch (Exception ex) when (count > 0)
        {
            Log.Warning(ex, "Reading a YouTube playlist stopped after {Count} tracks", count);
        }

        return count == 0
            ? new(MusicAddStatus.NotFound)
            : new(MusicAddStatus.Added, count, first, firstIndex);
    }

    // The track resolves while the bot joins, so it plays as soon as the voice connection is ready.
    public async Task<MusicAddResult> AddTrackAsync(
        IMusicPlayer mp,
        ulong guildId,
        ulong? joinChannelId,
        string query,
        string queuer,
        bool asNext)
    {
        var resolveTask = ResolveTrackInternalAsync(query);
        if (joinChannelId is { } channelId && !await EnsureVoiceChannelAsync(guildId, channelId))
            return new(MusicAddStatus.JoinFailed);

        if (await resolveTask is not { } track)
            return new(MusicAddStatus.NotFound);

        var (queued, index) = mp.EnqueueResolvedTrack(track, queuer, asNext, false);
        if (queued is null)
            return new(MusicAddStatus.QueueFull);

        AttachProxy(guildId);
        if (queued.Platform == MusicPlatform.Youtube
            && _ytResolver.GetYoutubeResolver() is ServiceYoutubeResolver service)
            _ = service.Service.PrefetchAsync(queued.TrackInfo.Id);

        return new(MusicAddStatus.Added, 1, queued, index);
    }

    public async Task<MusicAddResult> AddYoutubePlaylistAsync(
        IMusicPlayer mp,
        ulong guildId,
        ulong? joinChannelId,
        string query,
        string queuer)
    {
        if (joinChannelId is { } channelId && !await EnsureVoiceChannelAsync(guildId, channelId))
            return new(MusicAddStatus.JoinFailed);

        AttachProxy(guildId);
        try
        {
            return await EnqueueYoutubePlaylistAsync(mp, query, queuer);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading a YouTube playlist failed");
            return new(MusicAddStatus.NotFound);
        }
    }

    // Local files need forcePlatform, so this never resolves one.
    private async Task<ITrackInfo?> ResolveTrackInternalAsync(string query)
    {
        try
        {
            return await _trackResolveProvider.QuerySongAsync(query, null, false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Resolving a track failed");
            return null;
        }
    }

    public Task WaitPlaylistLoadAsync()
        => _playlistLoadLock.WaitAsync();

    public bool TryEnterPlaylistLoad()
        => _playlistLoadLock.Wait(0);

    public void ExitPlaylistLoad()
        => _playlistLoadLock.Release();

    public void StartPlaylistLoad(
        IMusicPlayer mp,
        IReadOnlyList<(string Query, MusicPlatform Platform)> songs,
        string queuer)
        => _ = Task.Run(async () =>
        {
            try
            {
                await mp.EnqueueManyAsync(songs, queuer, false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Loading a saved playlist failed");
            }
            finally
            {
                _playlistLoadLock.Release();
            }
        });

    // Counts and inserts in one statement, so parallel saves of one member cannot pass the cap.
    public async Task<int?> SavePlaylistAsync(
        string name,
        ulong authorId,
        string authorName,
        IReadOnlyList<PlaylistSong> songs)
    {
        await using var uow = _db.GetDbContext();
        await using var tx = await uow.Database.BeginTransactionAsync();
        await using var lctx = uow.CreateLinqToDBContext();
        var playlists = lctx.GetTable<MusicPlaylist>();
        var now = DateTime.UtcNow;
        var ids = await lctx.SelectQuery(() => 1)
                           .Where(_ => playlists.Count(p => p.AuthorId == authorId) < MAX_PLAYLISTS_PER_USER)
                           .InsertWithOutputAsync(playlists,
                               _ => new MusicPlaylist
                               {
                                   Name = name,
                                   Author = authorName,
                                   AuthorId = authorId,
                                   DateAdded = now
                               },
                               p => p.Id);

        if (ids.Length == 0)
            return null;

        var saved = new MusicPlaylist { Id = ids[0] };
        uow.Attach(saved);
        saved.Songs.AddRange(songs);
        await uow.SaveChangesAsync();
        await tx.CommitAsync();
        return saved.Id;
    }

    public const int PLAYLISTS_PER_PAGE = 20;

    // The shadow key of PlaylistSong. linq2db sees no association for it, so queries name the column.
    public const string PLAYLIST_KEY = "MusicPlaylistId";

    public async Task<int> CountPlaylistsAsync(ulong? authorId)
    {
        await using var uow = _db.GetDbContext();
        var playlists = uow.GetTable<MusicPlaylist>();
        return authorId is { } id
            ? await playlists.CountAsyncLinqToDB(p => p.AuthorId == id)
            : await playlists.CountAsyncLinqToDB();
    }

    // Counts the songs in SQL, so a page never loads the songs themselves.
    public async Task<IReadOnlyCollection<SavedPlaylistInfo>> GetPlaylistsPageAsync(ulong? authorId, int page)
    {
        if (page < 0 || page > int.MaxValue / PLAYLISTS_PER_PAGE)
            return [];

        await using var uow = _db.GetDbContext();
        var playlists = uow.GetTable<MusicPlaylist>();
        var songs = uow.GetTable<PlaylistSong>();
        var query = authorId is { } id ? playlists.Where(p => p.AuthorId == id) : playlists;
        var rows = await query
                         .OrderBy(static p => p.Id)
                         .Skip(page * PLAYLISTS_PER_PAGE)
                         .Take(PLAYLISTS_PER_PAGE)
                         .Select(p => new
                         {
                             p.Id,
                             p.Name,
                             p.Author,
                             SongCount = songs.Count(s => Sql.Property<int?>(s, PLAYLIST_KEY) == p.Id)
                         })
                         .ToArrayAsyncLinqToDB();

        var result = new SavedPlaylistInfo[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            result[i] = new(r.Id, r.Name ?? string.Empty, r.Author ?? string.Empty, r.SongCount);
        }

        return result;
    }

    public int MaxQueueSize
        => _musicConfig.Data.MaxQueueSize;

    public async Task EnqueueDirectoryAsync(IMusicPlayer mp, string dirPath, string queuer, bool allowLocalNetwork)
    {
        await foreach (var track in _localResolver.ResolveDirectoryAsync(dirPath))
        {
            if (mp.IsKilled || mp.IsQueueFull)
                break;

            mp.EnqueueTrack(track, queuer, allowLocalNetwork);
        }
    }

    private async Task<IMusicPlayer> CreateMusicPlayerInternalAsync(ulong guildId, ITextChannel defaultChannel)
    {
        var queue = new MusicQueue();
        var resolver = _trackResolveProvider;

        _voiceStateService.TryGetProxy(guildId, out var proxy);

        var settings = await GetSettingsInternalAsync(guildId);

        ITextChannel? overrideChannel = null;
        if (settings.MusicChannelId is { } channelId)
        {
            overrideChannel = _client.GetGuild(guildId)?.GetTextChannel(channelId);

            if (overrideChannel is null)
                Log.Warning("Saved music output channel doesn't exist, falling back to current channel");
        }

        _outputChannels[guildId] = (defaultChannel, overrideChannel);

        var mp = new MusicPlayer(queue,
            resolver,
            _ytResolver,
            _localResolver,
            proxy,
            _googleApiService,
            _audioFileCache,
            _musicConfig,
            _publicProxy,
            settings.AutoPlay);

        mp.SetRepeat(settings.PlayerRepeat);

        if (settings.Volume is >= 0 and <= 100)
            mp.SetVolume(settings.Volume);
        else
            Log.Error("Saved Volume is outside of valid range >= 0 && <=100 ({Volume})", settings.Volume);

        mp.OnCompleted += OnTrackCompleted(guildId);
        mp.OnStarted += OnTrackStarted(guildId);
        mp.OnQueueStopped += OnQueueStopped(guildId);

        return mp;
    }

    public async Task<IUserMessage?> SendToOutputAsync(ulong guildId, EmbedBuilder embed)
    {
        if (!_outputChannels.TryGetValue(guildId, out var chan))
            return null;

        var channel = chan.Override ?? chan.Default;
        if (!CanSendEmbedInternal(channel))
            return null;

        return await _sender.Response(channel)
                            .Embed(embed)
                            .SendAsync();
    }

    // Each track sends messages, so a channel which lost a permission must not cost a 403 for each one.
    private static bool CanSendEmbedInternal(ITextChannel channel)
    {
        if (channel is not SocketGuildChannel guildChannel || guildChannel.Guild.CurrentUser is not { } me)
            return true;

        // Discord computes the permissions of a thread from its parent channel, which does not show thread membership.
        if (channel is SocketThreadChannel thread)
            return thread.ParentChannel is { } parent
                   && CanSendEmbed(me.GetPermissions(parent),
                       thread: true,
                       outsidePrivateThread: thread.IsPrivateThread && !thread.HasJoined,
                       me.TimedOutUntil,
                       DateTimeOffset.UtcNow);

        return CanSendEmbed(me.GetPermissions(guildChannel),
            thread: false,
            outsidePrivateThread: false,
            me.TimedOutUntil,
            DateTimeOffset.UtcNow);
    }

    // A timeout is not part of the channel permissions, but Discord refuses each message while it lasts.
    public static bool CanSendEmbed(
        ChannelPermissions perms,
        bool thread,
        bool outsidePrivateThread,
        DateTimeOffset? timedOutUntil,
        DateTimeOffset now)
        => !outsidePrivateThread
           && (timedOutUntil is not { } until || until <= now)
           && perms.ViewChannel
           && (thread ? perms.SendMessagesInThreads : perms.SendMessages)
           && perms.EmbedLinks;

    private Func<IMusicPlayer, IQueuedTrackInfo, Task> OnTrackCompleted(ulong guildId)
    {
        IUserMessage? lastFinishedMessage = null;
        return async (mp, trackInfo) =>
        {
            _ = lastFinishedMessage?.DeleteAsync();
            var embed = _sender.CreateEmbed(guildId)
                               .WithOkColor()
                               .WithAuthor(GetText(guildId, strs.finished_track), Music.MUSIC_ICON_URL)
                               .WithDescription(trackInfo.PrettyName())
                               .WithFooter(trackInfo.PrettyTotalTime());

            lastFinishedMessage = await SendToOutputAsync(guildId, embed);
        };
    }

    private Func<IMusicPlayer, IQueuedTrackInfo, int, Task> OnTrackStarted(ulong guildId)
    {
        IUserMessage? lastPlayingMessage = null;
        return async (mp, trackInfo, index) =>
        {
            _ = lastPlayingMessage?.DeleteAsync();
            var embed = _sender.CreateEmbed(guildId)
                               .WithOkColor()
                               .WithAuthor(GetText(guildId, strs.playing_track(index + 1)), Music.MUSIC_ICON_URL)
                               .WithDescription(trackInfo.PrettyName())
                               .WithFooter($"{mp.PrettyVolume()} | {trackInfo.PrettyInfo()}");

            lastPlayingMessage = await SendToOutputAsync(guildId, embed);
        };
    }

    private Func<IMusicPlayer, Task> OnQueueStopped(ulong guildId)
        => _ =>
        {
            if (_settings.TryGetValue(guildId, out var settings))
            {
                if (settings.AutoDisconnect)
                    return LeaveVoiceChannelAsync(guildId);
            }

            return Task.CompletedTask;
        };

    // this has to be done because dragging bot to another vc isn't supported yet
    public async Task<bool> PlayAsync(ulong guildId, ulong voiceChannelId)
    {
        if (!TryGetMusicPlayer(guildId, out var mp))
            return false;

        if (mp.IsStopped)
        {
            if (!_voiceStateService.TryGetProxy(guildId, out var proxy)
                || proxy.State == VoiceProxy.VoiceProxyState.Stopped)
            {
                await JoinLockedInternalAsync(guildId, voiceChannelId, onlyWhenNeeded: false);
                AttachProxy(guildId);
            }
        }

        mp.Next();
        return true;
    }

    public async Task<IList<(string Title, string Url, string Thumbnail)>> SearchVideosAsync(string query)
    {
        var tracks = (await SearchTracksAsync(query, SEARCH_RESULTS)).Tracks;
        var results = new (string, string, string)[tracks.Count];
        for (var i = 0; i < results.Length; i++)
        {
            var t = tracks[i];
            results[i] = (t.Title, t.Url, t.Thumbnail);
        }

        return results;
    }

    public async Task<MusicSearchResult> SearchTracksAsync(string query, int limit)
    {
        limit = Math.Clamp(limit, 1, SEARCH_RESULTS);

        // A bot which uses the music service does not connect to YouTube or to the Google API.
        if (_ytResolver.GetYoutubeResolver() is ServiceYoutubeResolver service)
        {
            if (await service.Service.TrySearchAsync(query, limit) is not { } found)
                return new([], true);

            var tracks = new ITrackInfo[Math.Min(found.Length, limit)];
            for (var i = 0; i < tracks.Length; i++)
                tracks[i] = ServiceYoutubeResolver.ToTrackInfo(found[i]);

            return new(tracks, false);
        }

        var failed = true;
        try
        {
            if (await _ytLoader.LoadResultsAsync(query) is { } videos)
            {
                failed = false;
                if (videos.Count > 0)
                {
                    var tracks = new ITrackInfo[Math.Min(videos.Count, limit)];
                    for (var i = 0; i < tracks.Length; i++)
                    {
                        var v = videos[i];
                        tracks[i] = new RemoteTrackInfo(v is YtLoader.YtTrackInfo yt ? yt.Id : string.Empty,
                            v.Title,
                            v.Url,
                            v.Thumb,
                            v.Duration,
                            MusicPlatform.Youtube);
                    }

                    return new(tracks, false);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed geting videos with YtLoader: {ErrorMessage}", ex.Message);
        }

        try
        {
            var tracks = new List<ITrackInfo>(limit);
            foreach (var v in await _googleApiService.GetVideoInfosByKeywordAsync(query, limit))
                tracks.Add(new RemoteTrackInfo(v.Id, v.Name, v.Url, v.Thumbnail, TimeSpan.Zero, MusicPlatform.Youtube));

            return new(tracks, false);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed getting video results with Google Api. "
                        + "Probably google api key missing: {ErrorMessage}",
                ex.Message);
        }

        return new([], failed);
    }

    private string GetText(ulong guildId, LocStr str)
        => _strings.GetText(str, guildId);

    public IEnumerable<(string Name, Func<string> Func)> GetPlaceholders()
    {
        // random track that's playing
        yield return ("%music.playing%", () =>
        {
            var randomPlayingTrack = _players.Select(x => x.Value.GetCurrentTrack(out _))
                                             .Where(x => x is not null)
                                             .Shuffle()
                                             .FirstOrDefault();

            if (randomPlayingTrack is null)
                return "-";

            return randomPlayingTrack.Title;
        });

        // number of servers currently listening to music
        yield return ("%music.servers%", () =>
        {
            var count = _players.Select(x => x.Value.GetCurrentTrack(out _)).Count(x => x is not null);

            return count.ToString();
        });

        yield return ("%music.queued%", () =>
        {
            var count = _players.Sum(x => x.Value.GetQueuedTracks().Count);

            return count.ToString();
        });
    }

    #region Settings

    private async Task<MusicPlayerSettings> GetSettingsInternalAsync(ulong guildId)
    {
        if (_settings.TryGetValue(guildId, out var settings))
            return settings;

        await using var uow = _db.GetDbContext();
        var toReturn = _settings[guildId] = await uow.Set<MusicPlayerSettings>().ForGuildAsync(guildId);
        await uow.SaveChangesAsync();

        return toReturn;
    }

    public async Task<MusicPlayerSettings> GetSavedSettingsAsync(ulong guildId)
    {
        if (_settings.TryGetValue(guildId, out var cached))
            return cached;

        await using var uow = _db.GetDbContext();
        return await uow.GetTable<MusicPlayerSettings>()
                        .Where(x => x.GuildId == guildId)
                        .FirstOrDefaultAsyncLinqToDB()
               ?? new MusicPlayerSettings { GuildId = guildId };
    }

    private async Task ModifySettingsInternalAsync<TState>(
        ulong guildId,
        Action<MusicPlayerSettings, TState> action,
        TState state)
    {
        await using var uow = _db.GetDbContext();
        var ms = await uow.Set<MusicPlayerSettings>().ForGuildAsync(guildId);
        action(ms, state);
        await uow.SaveChangesAsync();
        _settings[guildId] = ms;
    }

    public async Task<bool> SetMusicChannelAsync(ulong guildId, ulong? channelId)
    {
        if (channelId is null)
        {
            await UnsetMusicChannelAsync(guildId);
            return true;
        }

        var channel = _client.GetGuild(guildId)?.GetTextChannel(channelId.Value);
        if (channel is null)
            return false;

        await ModifySettingsInternalAsync(guildId,
            (settings, chId) => { settings.MusicChannelId = chId; },
            channelId);

        _outputChannels.AddOrUpdate(guildId, (channel, channel), (_, old) => (old.Default, channel));

        return true;
    }

    public async Task UnsetMusicChannelAsync(ulong guildId)
    {
        await ModifySettingsInternalAsync(guildId,
            (settings, _) => { settings.MusicChannelId = null; },
            (ulong?)null);

        if (_outputChannels.TryGetValue(guildId, out var old))
            _outputChannels[guildId] = (old.Default, null);
    }

    public async Task SetRepeatAsync(ulong guildId, PlayerRepeatType repeatType)
    {
        await ModifySettingsInternalAsync(guildId,
            (settings, type) => { settings.PlayerRepeat = type; },
            repeatType);

        if (TryGetMusicPlayer(guildId, out var mp))
            mp.SetRepeat(repeatType);
    }

    public async Task SetVolumeAsync(ulong guildId, int value)
    {
        if (value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value));

        await ModifySettingsInternalAsync(guildId,
            (settings, newValue) => { settings.Volume = newValue; },
            value);

        if (TryGetMusicPlayer(guildId, out var mp))
            mp.SetVolume(value);
    }

    public async Task<bool> ToggleAutoDisconnectAsync(ulong guildId)
    {
        var newState = false;
        await ModifySettingsInternalAsync(guildId,
            (settings, _) => { newState = settings.AutoDisconnect = !settings.AutoDisconnect; },
            default(object));

        return newState;
    }

    public async Task<bool> ToggleQueueAutoPlayAsync(ulong guildId)
    {
        var newValue = false;
        await ModifySettingsInternalAsync(guildId,
            (settings, _) =>
            {
                newValue = settings.AutoPlay = !settings.AutoPlay;
                if (newValue)
                    settings.PlayerRepeat = PlayerRepeatType.None;
            },
            false);

        if (TryGetMusicPlayer(guildId, out var mp))
        {
            mp.AutoPlay = newValue;
            if (newValue)
                mp.SetRepeat(PlayerRepeatType.None);
        }

        return newValue;
    }

    public Task<bool> FairplayAsync(ulong guildId)
    {
        if (TryGetMusicPlayer(guildId, out var mp))
        {
            mp.SetFairplay();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<IQueuedTrackInfo?> RemoveLastQueuedTrackAsync(ulong guildId)
    {
        if (TryGetMusicPlayer(guildId, out var mp))
        {
            var last = await mp.RemoveLastQueuedTrack();

            return last;
        }

        return null;
    }

    #endregion
}