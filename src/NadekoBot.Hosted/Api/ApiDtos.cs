using System.Text.Json.Serialization;

namespace NadekoBot.Hosted;

public sealed record HealthDto(
    string Status,
    int ShardId,
    int TotalShards,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    UpdateResultDto? Update = null);

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
    IReadOnlyList<string>? Reactions = null);

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

public sealed record LogLineDto(DateTime Ts, string Level, string Text);

public sealed record UpdateAcceptedDto(string Version);

public sealed record DiskUsageDto(long UsedBytes, long TotalBytes);

public sealed record GuildHealthDto(HealthBotDto Bot, IReadOnlyList<HealthCheckDto> Checks, HealthTimersDto Timers);

public sealed record HealthBotDto(string? RoleId, int RolePosition, bool Administrator, IReadOnlyList<string> Missing);

public sealed record HealthCheckDto(
    string Feature,
    string Status,
    string? Issue,
    string? Permission,
    string? ChannelId,
    string? RoleId,
    int Count);

public sealed record HealthTimersDto(int Mutes, int Bans, int TempRoles, DateTime? NextExpiry);

public sealed record GuildActivityDto(
    DateTime? Since,
    IReadOnlyList<ActivityDayDto> Days,
    IReadOnlyList<TopCommandDto> TopCommands,
    IReadOnlyList<long> Hours);

public sealed record ActivityDayDto(DateOnly Date, long Commands, long Errors, long Joins, long Leaves, long Warnings);

public sealed record TopCommandDto(string Name, long Count);
