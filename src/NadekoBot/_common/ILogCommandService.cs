using NadekoBot.Db.Models;

namespace NadekoBot.Common;

public interface ILogCommandService
{
    void AddDeleteIgnore(ulong xId);
    Task LogServer(ulong guildId, ulong channelId, bool actionValue);
    bool LogIgnore(ulong guildId, ulong itemId, IgnoredItemType itemType);
    LogSetting? GetGuildLogSettings(ulong guildId);
    bool Log(ulong guildId, ulong? channelId, LogType type);
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
    ThreadCreated
}