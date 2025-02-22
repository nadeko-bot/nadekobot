using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Administration;

public interface INotifySubscriber
{
    Task NotifyAsync<T>(T data, bool isShardLocal = false)
        where T : struct, INotifyModel<T>;

    void RegisterModel<T>()
        where T : struct, INotifyModel<T>;
    
    NotifyModelData GetRegisteredModel(NotifyType nType);
}

public readonly record struct NotifyModelData(NotifyType Type, IReadOnlyList<string> Replacements);