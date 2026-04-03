using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public sealed class DummyLogCommandService : ILogCommandService
#if GLOBAL_NADEKO
, INService
#endif
{
    public void AddDeleteIgnore(ulong xId)
    {
    }

    public void AddBanIgnore(ulong guildId, ulong userId)
    {
    }

    public Task LogServer(ulong guildId, ulong channelId, bool actionValue)
        => Task.CompletedTask;

    public bool LogIgnore(ulong guildId, ulong itemId, IgnoredItemType itemType)
        => false;

    public IReadOnlyList<LogIgnore> GetLogIgnores(ulong guildId)
        => [];

    public ulong? GetLogChannelId(ulong guildId, LogType logType)
        => null;

    public bool Log(ulong guildId, ulong? channelId, LogType type)
        => false;

    public Task LogHoneypot(IGuild guild, IUser user)
        => Task.CompletedTask;
}
