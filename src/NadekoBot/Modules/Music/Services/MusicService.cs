using NadekoBot.Db.Models;
using NadekoBot.Modules.Music.Resolvers;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Music.Services;

public sealed class MusicService : IMusicService, IPlaceholderProvider
{
    private readonly AyuVoiceStateService _voiceStateService;
    private readonly ITrackResolveProvider _trackResolveProvider;
    private readonly DbService _db;
    private readonly IYoutubeResolverFactory _ytResolver;
    private readonly ILocalTrackResolver _localResolver;
    private readonly DiscordSocketClient _client;
    private readonly IBotStrings _strings;
    private readonly IGoogleApiService _googleApiService;
    private readonly YtLoader _ytLoader;
    private readonly IMessageSenderService _sender;

    private readonly ConcurrentDictionary<ulong, IMusicPlayer> _players;
    private readonly ConcurrentDictionary<ulong, (ITextChannel Default, ITextChannel? Override)> _outputChannels;
    private readonly ConcurrentDictionary<ulong, MusicPlayerSettings> _settings;

    public MusicService(
        AyuVoiceStateService voiceStateService,
        ITrackResolveProvider trackResolveProvider,
        DbService db,
        IYoutubeResolverFactory ytResolver,
        ILocalTrackResolver localResolver,
        DiscordSocketClient client,
        IBotStrings strings,
        IGoogleApiService googleApiService,
        YtLoader ytLoader,
        IMessageSenderService sender)
    {
        _voiceStateService = voiceStateService;
        _trackResolveProvider = trackResolveProvider;
        _db = db;
        _ytResolver = ytResolver;
        _localResolver = localResolver;
        _client = client;
        _strings = strings;
        _googleApiService = googleApiService;
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
        _ = Task.Delay(10_000).ContinueWith(_ => musicPlayer.Dispose());
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

    public async Task<IMusicPlayer?> GetOrCreateMusicPlayerAsync(ITextChannel contextChannel)
    {
        var newPLayer = await CreateMusicPlayerInternalAsync(contextChannel.GuildId, contextChannel);
        if (newPLayer is null)
            return null;

        return _players.GetOrAdd(contextChannel.GuildId, newPLayer);
    }

    public bool TryGetMusicPlayer(ulong guildId, [MaybeNullWhen(false)] out IMusicPlayer musicPlayer)
        => _players.TryGetValue(guildId, out musicPlayer);

    public async Task<int> EnqueueYoutubePlaylistAsync(IMusicPlayer mp, string query, string queuer)
    {
        var count = 0;
        await foreach (var track in _ytResolver.GetYoutubeResolver().ResolveTracksFromPlaylistAsync(query))
        {
            if (mp.IsKilled)
                break;

            mp.EnqueueTrack(track, queuer);
            ++count;
        }

        return count;
    }

    public async Task EnqueueDirectoryAsync(IMusicPlayer mp, string dirPath, string queuer)
    {
        await foreach (var track in _localResolver.ResolveDirectoryAsync(dirPath))
        {
            if (mp.IsKilled)
                break;

            mp.EnqueueTrack(track, queuer);
        }
    }

    private async Task<IMusicPlayer?> CreateMusicPlayerInternalAsync(ulong guildId, ITextChannel defaultChannel)
    {
        var queue = new MusicQueue();
        var resolver = _trackResolveProvider;

        if (!_voiceStateService.TryGetProxy(guildId, out var proxy))
            return null;

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
            proxy,
            _googleApiService,
            settings.QualityPreset,
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
        if (_outputChannels.TryGetValue(guildId, out var chan))
        {
            var msg = await _sender.Response(chan.Override ?? chan.Default)
                                   .Embed(embed)
                                   .SendAsync();
            return msg;
        }

        return null;
    }

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
                await JoinVoiceChannelAsync(guildId, voiceChannelId);
        }

        mp.Next();
        return true;
    }

    private async Task<IList<(string Title, string Url, string Thumb)>> SearchYtLoaderVideosAsync(string query)
    {
        var result = await _ytLoader.LoadResultsAsync(query);
        return result.Select(x => (x.Title, x.Url, x.Thumb)).ToList();
    }

    private async Task<IList<(string Title, string Url, string Thumb)>> SearchGoogleApiVideosAsync(string query)
    {
        var result = await _googleApiService.GetVideoInfosByKeywordAsync(query, 5);
        return result.Select(x => (x.Name, x.Url, x.Thumbnail)).ToList();
    }

    public async Task<IList<(string Title, string Url, string Thumbnail)>> SearchVideosAsync(string query)
    {
        try
        {
            IList<(string, string, string)> videos = await SearchYtLoaderVideosAsync(query);
            if (videos.Count > 0)
                return videos;
        }
        catch (Exception ex)
        {
            Log.Warning("Failed geting videos with YtLoader: {ErrorMessage}", ex.Message);
        }

        try
        {
            return await SearchGoogleApiVideosAsync(query);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed getting video results with Google Api. "
                        + "Probably google api key missing: {ErrorMessage}",
                ex.Message);
        }

        return Array.Empty<(string, string, string)>();
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

    public async Task<QualityPreset> GetMusicQualityAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var settings = await uow.Set<MusicPlayerSettings>().ForGuildAsync(guildId);
        return settings.QualityPreset;
    }

    public Task SetMusicQualityAsync(ulong guildId, QualityPreset preset)
        => ModifySettingsInternalAsync(guildId,
            (settings, _) => { settings.QualityPreset = preset; },
            preset);

    public async Task<bool> ToggleQueueAutoPlayAsync(ulong guildId)
    {
        var newValue = false;
        await ModifySettingsInternalAsync(guildId,
            (settings, _) => newValue = settings.AutoPlay = !settings.AutoPlay,
            false);

        if (TryGetMusicPlayer(guildId, out var mp))
            mp.AutoPlay = newValue;

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