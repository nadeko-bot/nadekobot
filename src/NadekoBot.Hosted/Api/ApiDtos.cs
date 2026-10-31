using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Hosted;

public sealed record HealthDto(
    string Status,
    int ShardId,
    int TotalShards,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    UpdateResultDto? Update = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] HealthIntentsDto? Intents = null);

// The privileged intents which the running bot asked Discord for.
public sealed record HealthIntentsDto(bool Presence, bool Members, bool MessageContent)
{
    public static HealthIntentsDto? From(IntentState? intents)
        => intents is null ? null : new(intents.Presence, intents.Members, intents.MessageContent);
}

// data/.update-result: Reason is set only for "failed" and "rollback_failed".
public sealed record UpdateResultDto(string Result, string Version, string? Reason);

public sealed record StatusDto(
    int TotalShards,
    long GuildCount,
    double MemoryMb,
    long UptimeSeconds,
    long CommandsRan,
    IReadOnlyList<ShardStatusDto> Shards);

public sealed record AiKeyStatusDto(bool Set);

public sealed record SetAiKeyRequest(string? Key);

public sealed record ShardStatusDto(
    int ShardId,
    string ConnectionState,
    int GuildCount);

public sealed record GuildListItemDto(
    string Id,
    string Name,
    string? IconUrl,
    int MemberCount,
    string OwnerId);

public sealed record GuildInfoDto(
    string Id,
    string Name,
    string? IconUrl,
    string OwnerId,
    string? OwnerName,
    int MemberCount,
    int TextChannels,
    int VoiceChannels,
    DateTime CreatedAt,
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<EmojiDto> Emojis,
    IReadOnlyList<string> Features);

public sealed record ChannelDto(string Id, string Name, string Type);

public sealed record RoleDto(string Id, string Name, string? IconUrl, string Color);

public sealed record EmojiDto(string Name, string Code, string Url);

public sealed record ConfigDto(string Name, IReadOnlyList<ConfigPropDto> Props);

// Type is one of ConfigPropTypes. Min and Max are inclusive; Options is set only for "choice".
public sealed record ConfigPropDto(
    string Prop,
    string? Value,
    string? Comment,
    string Type,
    double? Min,
    double? Max,
    int? MaxLength,
    IReadOnlyList<string>? Options);

public sealed record SetConfigRequest(string Value);

public sealed record GreetDto(
    string Type,
    bool Enabled,
    string? ChannelId,
    string Message,
    int AutoDeleteTimer);

public sealed record UpdateGreetRequest(
    bool? Enabled = null,
    string? ChannelId = null,
    string? Message = null,
    int? AutoDeleteTimer = null);

public sealed record TestGreetRequest(string? ChannelId = null, string? UserId = null);

public sealed record ExprDto(
    int Id,
    string KwumId,
    string Trigger,
    string Response,
    bool AllowTarget,
    bool ContainsAnywhere,
    bool DmResponse,
    bool AutoDeleteTrigger,
    IReadOnlyList<string> Reactions);

public sealed record ExprListDto(int Total, IReadOnlyList<ExprDto> Items);

public sealed record CreateExprRequest(
    string Trigger,
    string Response,
    bool? Ca = null,
    bool? Ad = null,
    bool? Dm = null);

public sealed record UpdateExprRequest(
    string? Response = null,
    bool? Ca = null,
    bool? Ad = null,
    bool? Dm = null,
    IReadOnlyList<string>? Reactions = null,
    string? Trigger = null,
    bool? Replace = null);

public sealed record QuoteDto(
    int Id,
    string KwumId,
    string Keyword,
    string Text,
    string AuthorName,
    string AuthorId);

public sealed record QuoteSearchDto(int Total, IReadOnlyList<QuoteDto> Items);

public sealed record CreateQuoteRequest(string Keyword, string Text, string? AuthorId = null);

public sealed record UpdateQuoteRequest(string? Keyword = null, string? Text = null);

public sealed record CountDto(int Count);

public sealed record FeedEventDto(
    long Id,
    string Type,
    DateTime Timestamp,
    string? GuildId,
    string? ChannelId,
    string UserId,
    string Username,
    string Command,
    string Module,
    string Message,
    string? Error);

public sealed record BackupDto(string FileName, long SizeBytes, DateTime CreatedAt);

public sealed record ServerImportDto(BackupDto Backup, bool XpIncluded, long Rows);

public sealed record WarningDto(
    int Id,
    string UserId,
    string Reason,
    bool Forgiven,
    string? ForgivenBy,
    string Moderator,
    long Weight,
    DateTime? DateAdded);

public sealed record WarningListDto(int Total, IReadOnlyList<WarningDto> Items);

public sealed record ForgiveWarningRequest(string? Moderator = null);

public sealed record WarnPunishmentDto(int Count, string Punishment, int TimeMinutes, string? RoleId);

public sealed record SetWarnPunishmentRequest(string Punishment, int? TimeMinutes = null, string? RoleId = null);

public sealed record WarnExpiryDto(int Days, bool Delete);

public sealed record SetWarnExpiryRequest(int Days, bool Delete);

public sealed record XpUserDto(
    int Rank,
    string UserId,
    string? Username,
    long Level,
    long LevelXp,
    long RequiredXp,
    long TotalXp);

public sealed record XpLeaderboardDto(int Total, IReadOnlyList<XpUserDto> Items);

public sealed record XpRoleRewardDto(int Level, string RoleId, bool Remove);

public sealed record XpCurrencyRewardDto(int Level, int Amount);

public sealed record XpRewardsDto(
    IReadOnlyList<XpRoleRewardDto> RoleRewards,
    IReadOnlyList<XpCurrencyRewardDto> CurrencyRewards);

public sealed record SetXpRoleRewardRequest(string RoleId, bool? Remove = null);

public sealed record SetXpCurrencyRewardRequest(int Amount);

public sealed record AddXpRequest(long Amount);

public sealed record SetXpLevelRequest(int Level);

public sealed record ErrorDto(string Error);

public sealed record SetCredsRequest(string ApiKey, string? Token = null, string?[]? OwnerIds = null);

public sealed record OwnerDto(
    string Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null);

public sealed record OwnersDto(IReadOnlyList<OwnerDto> Owners);

public sealed record SetOwnersRequest(string?[]? Ids = null);

// Url is set only for "streaming".
public sealed record PresenceDto(string Status, PresenceActivityDto? Activity, bool Rotating);

public sealed record PresenceActivityDto(string Type, string Text, string? Url);

// Activity must be in the body; null removes the activity.
public sealed record SetPresenceRequest(string? Status, SetPresenceActivityRequest? Activity);

public sealed record SetPresenceActivityRequest(string? Type, string? Text, string? Url = null);

// Template and Props rebuild the message part of Text; ExceptionAt is the index of the \n before the exception.
public sealed record LogLineDto(
    DateTime Ts,
    string Level,
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Template = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Props = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ExceptionAt = null);

public sealed record UpdateAcceptedDto(string Version);

public sealed record DiskUsageDto(long UsedBytes, long TotalBytes);

public sealed record ToolDto(string Name, string Version, string Digest, long InstalledAt);

public sealed record ToolListDto(ToolDto[] Tools);

public sealed record SetYoutubeProxyRequest(string? Id, string? Url);

public sealed record SetMusicServiceRequest(string? Id, string? Url, string? Key);

// Never the key: the bot owner must not see it.
public sealed record MusicServiceDto(string? Id, string? Url);

public sealed record YoutubeCountsDto(
    long Ok,
    long BotCheck,
    long RateLimited,
    long Forbidden,
    long ProxyErrors,
    long NoJsRuntime,
    long OtherErrors,
    long ProxyBytes);

public sealed record YoutubeStatusDto(string? ProxyId, YoutubeCountsDto Hour, YoutubeCountsDto Day);

// DurationSec is 0 when the length is not known.
public sealed record MusicTrackDto(string Id, string Title, string Url, string? Thumbnail, int DurationSec);

public sealed record MusicSearchDto(IReadOnlyList<MusicTrackDto> Tracks);

// Platform is "youtube", "radio" or "local".
public sealed record QueuedTrackDto(
    int Index,
    string Id,
    string Title,
    string Url,
    string? Thumbnail,
    int DurationSec,
    string Platform,
    string Queuer);

// Repeat is "none", "track" or "queue".
public sealed record MusicStateDto(
    string? VoiceChannelId,
    bool Player,
    bool Paused,
    bool Stopped,
    string Repeat,
    bool Autoplay,
    int Volume,
    int? CurrentIndex,
    int MaxQueueSize,
    IReadOnlyList<QueuedTrackDto> Tracks);

public sealed record AddTracksRequest(
    string? Query = null,
    bool? Playlist = null,
    bool? Next = null,
    string? VoiceChannelId = null,
    string? By = null);

public sealed record AddTracksDto(int Added, QueuedTrackDto? First);

public sealed record MoveTrackRequest(int From, int To);

public sealed record PlayTrackRequest(int Index);

public sealed record UpdateMusicRequest(bool? Paused = null, string? Repeat = null, int? Volume = null);

public sealed record LoadPlaylistRequest(string? VoiceChannelId = null, bool? Shuffle = null, string? By = null);

public sealed record LoadStartedDto(int SongCount);

public sealed record PlaylistInfoDto(int Id, string Name, string AuthorId, string AuthorName, int SongCount);

public sealed record PlaylistPageDto(int Total, IReadOnlyList<PlaylistInfoDto> Playlists);

public sealed record PlaylistSongDto(string Title, string Url);

public sealed record PlaylistDto(
    int Id,
    string Name,
    string AuthorId,
    string AuthorName,
    int SongCount,
    IReadOnlyList<PlaylistSongDto> Songs);

public sealed record SavePlaylistSongRequest(string? Title = null, string? Url = null);

public sealed record SavePlaylistRequest(
    string? Name = null,
    SavePlaylistSongRequest?[]? Songs = null,
    string? By = null);

public sealed record GuildHealthDto(HealthBotDto Bot, IReadOnlyList<HealthCheckDto> Checks, HealthTimersDto Timers);

public sealed record HealthBotDto(string? RoleId, int RolePosition, bool Administrator, IReadOnlyList<string> Missing);

public sealed record HealthCheckDto(
    string Feature,
    string Status,
    string? Issue,
    string? Permission,
    string? ChannelId,
    string? RoleId,
    int Count,
    string? Intent = null);

public sealed record HealthTimersDto(int Mutes, int Bans, int TempRoles, DateTime? NextExpiry);

public sealed record GuildActivityDto(
    DateTime? Since,
    IReadOnlyList<ActivityDayDto> Days,
    IReadOnlyList<TopCommandDto> TopCommands,
    IReadOnlyList<long> Hours);

public sealed record ActivityDayDto(DateOnly Date, long Commands, long Errors, long Joins, long Leaves, long Warnings);

public sealed record TopCommandDto(string Name, long Count);
public sealed record LimitReachedDto(string Error, int Max);

public sealed record PlatformErrorDto(string Error, string Platform);

// FailingSince is the first failed check of the current streak, null while the feed works.
public sealed record FeedSubDto(int Id, string Url, string ChannelId, string? Message, DateTime? FailingSince);

public sealed record FeedListDto(IReadOnlyList<FeedSubDto> Feeds, int Max);

public sealed record CreateFeedRequest(string? Url = null, string? ChannelId = null, string? Message = null);

// Message is a JsonElement so that a missing field keeps the message and null clears it.
public sealed record UpdateFeedRequest(string? ChannelId = null, JsonElement Message = default);

// Platform is "twitch", "picarto", "youtube", "facebook", "trovo" or "kick".
public sealed record FollowedStreamDto(int Id, string Platform, string Name, string Url, string ChannelId, string? Message);

public sealed record StreamListDto(
    IReadOnlyList<FollowedStreamDto> Streams,
    int Max,
    bool NotifyOffline,
    bool DeleteOnlineMessage);

public sealed record CreateStreamRequest(string? Url = null, string? ChannelId = null, string? Message = null);

public sealed record UpdateStreamRequest(string? ChannelId = null, JsonElement Message = default);

public sealed record StreamSettingsDto(bool NotifyOffline, bool DeleteOnlineMessage);

public sealed record UpdateStreamSettingsRequest(bool? NotifyOffline = null, bool? DeleteOnlineMessage = null);
