using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public interface INotifyModel<T>
    where T: struct, INotifyModel<T>
{
    static abstract string KeyName { get; }
    static abstract NotifyType NotifyType {get;}
    static abstract IReadOnlyList<NotifyModelPlaceholderData<T>> GetReplacements();

    public virtual bool TryGetGuildId(out ulong guildId)
    {
        guildId = 0;
        return false;
    }
    
    public virtual bool TryGetUserId(out ulong userId)
    {
        userId = 0;
        return false;
    }
}

public readonly record struct NotifyModelPlaceholderData<T>(string Name, Func<T, SocketGuild, string> Func);