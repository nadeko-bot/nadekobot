using NadekoBot.Db.Models;

namespace NadekoBot.Common;

public interface ILogCommandService
{
    void AddDeleteIgnore(ulong xId);
    void AddBanIgnore(ulong guildId, ulong userId);
    Task LogServer(ulong guildId, ulong channelId, bool actionValue);
    bool LogIgnore(ulong guildId, ulong itemId, IgnoredItemType itemType);
    IReadOnlyList<LogIgnore> GetLogIgnores(ulong guildId);
    ulong? GetLogChannelId(ulong guildId, LogType logType);
    bool Log(ulong guildId, ulong? channelId, LogType type);
    Task LogHoneypot(IGuild guild, IUser user);
}

public enum LogType
{
    Other,
    MessageUpdated,
    MessageDeleted,
    UserJoined,
    UserLeft,
    UserBanned,
    UserUnbanned,
    UserUpdated,
    ChannelCreated,
    ChannelDestroyed,
    ChannelUpdated,
    UserPresence,
    VoicePresence,
    UserMuted,
    UserWarned,
    ThreadDeleted,
    ThreadCreated,
    Honeypot
}
