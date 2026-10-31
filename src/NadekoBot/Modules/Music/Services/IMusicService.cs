using NadekoBot.Db.Models;
using System.Diagnostics.CodeAnalysis;

namespace NadekoBot.Modules.Music.Services;

public interface IMusicService
{
    /// <summary>
    ///     Leave voice channel in the specified guild if it's connected to one
    /// </summary>
    /// <param name="guildId">Id of the guild</param>
    public Task LeaveVoiceChannelAsync(ulong guildId);

    /// <summary>
    ///     Joins the voice channel with the specified id
    /// </summary>
    /// <param name="guildId">Id of the guild where the voice channel is</param>
    /// <param name="voiceChannelId">Id of the voice channel</param>
    public Task JoinVoiceChannelAsync(ulong guildId, ulong voiceChannelId);

    // Joins when the bot is in no voice channel or the server has no player. False when Discord did not confirm the join.
    Task<bool> EnsureVoiceChannelAsync(ulong guildId, ulong voiceChannelId);

    // followChannel moves the default output of an existing player to this channel, as a music command does.
    Task<IMusicPlayer> GetOrCreateMusicPlayerAsync(ITextChannel defaultChannel, bool followChannel = true);
    bool TryGetMusicPlayer(ulong guildId, [MaybeNullWhen(false)] out IMusicPlayer musicPlayer);
    void AttachProxy(ulong guildId);
    Task<MusicAddResult> EnqueueYoutubePlaylistAsync(IMusicPlayer mp, string query, string queuer);

    // joinChannelId is set when the bot must join before the track can play.
    Task<MusicAddResult> AddTrackAsync(
        IMusicPlayer mp,
        ulong guildId,
        ulong? joinChannelId,
        string query,
        string queuer,
        bool asNext);

    Task<MusicAddResult> AddYoutubePlaylistAsync(
        IMusicPlayer mp,
        ulong guildId,
        ulong? joinChannelId,
        string query,
        string queuer);

    Task<MusicSearchResult> SearchTracksAsync(string query, int limit);

    // The saved settings which a new player uses; reading creates no row.
    Task<MusicPlayerSettings> GetSavedSettingsAsync(ulong guildId);
    int MaxQueueSize { get; }
    Task<int> CountPlaylistsAsync(ulong? authorId);
    Task<IReadOnlyCollection<SavedPlaylistInfo>> GetPlaylistsPageAsync(ulong? authorId, int page);
    Task<int?> SavePlaylistAsync(string name, ulong authorId, string authorName, IReadOnlyList<PlaylistSong> songs);

    // One saved playlist loads at a time on a shard, because each song is a resolver call.
    Task WaitPlaylistLoadAsync();
    bool TryEnterPlaylistLoad();
    void ExitPlaylistLoad();

    // Takes over the load lock which the caller entered and releases it when the songs are queued.
    void StartPlaylistLoad(IMusicPlayer mp, IReadOnlyList<(string Query, MusicPlatform Platform)> songs, string queuer);
    Task EnqueueDirectoryAsync(IMusicPlayer mp, string dirPath, string queuer, bool allowLocalNetwork);
    Task<IUserMessage?> SendToOutputAsync(ulong guildId, EmbedBuilder embed);
    Task<bool> PlayAsync(ulong guildId, ulong voiceChannelId);
    Task<IList<(string Title, string Url, string Thumbnail)>> SearchVideosAsync(string query);
    Task<bool> SetMusicChannelAsync(ulong guildId, ulong? channelId);
    Task SetRepeatAsync(ulong guildId, PlayerRepeatType repeatType);
    Task SetVolumeAsync(ulong guildId, int value);
    Task<bool> ToggleAutoDisconnectAsync(ulong guildId);
    Task<bool> ToggleQueueAutoPlayAsync(ulong guildId);
    Task<bool> FairplayAsync(ulong guildId);
    Task<IQueuedTrackInfo?> RemoveLastQueuedTrackAsync(ulong guildId);
}