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

    Task<IMusicPlayer?> GetOrCreateMusicPlayerAsync(ITextChannel contextChannel);
    bool TryGetMusicPlayer(ulong guildId, [MaybeNullWhen(false)] out IMusicPlayer musicPlayer);
    Task<int> EnqueueYoutubePlaylistAsync(IMusicPlayer mp, string playlistId, string queuer);
    Task EnqueueDirectoryAsync(IMusicPlayer mp, string dirPath, string queuer);
    Task<IUserMessage?> SendToOutputAsync(ulong guildId, EmbedBuilder embed);
    Task<bool> PlayAsync(ulong guildId, ulong voiceChannelId);
    Task<IList<(string Title, string Url, string Thumbnail)>> SearchVideosAsync(string query);
    Task<bool> SetMusicChannelAsync(ulong guildId, ulong? channelId);
    Task SetRepeatAsync(ulong guildId, PlayerRepeatType repeatType);
    Task SetVolumeAsync(ulong guildId, int value);
    Task<bool> ToggleAutoDisconnectAsync(ulong guildId);
    Task<QualityPreset> GetMusicQualityAsync(ulong guildId);
    Task SetMusicQualityAsync(ulong guildId, QualityPreset preset);
    Task<bool> ToggleQueueAutoPlayAsync(ulong guildId);
    Task<bool> FairplayAsync(ulong guildId);
    Task<IQueuedTrackInfo?> RemoveLastQueuedTrackAsync(ulong guildId);
}