namespace NadekoBot.Modules.Administration;

public static class NotifyModelExtensions
{
    public static TypedKey<T> GetTypedKey<T>(this T model)
        where T : struct, INotifyModel<T>
        => new(T.KeyName);
}