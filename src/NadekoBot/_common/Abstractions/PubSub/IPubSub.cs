namespace Nadeko.Common;

public interface IPubSub
{
    public Task Pub<TData>(in TypedKey<TData> key, TData data)
        where TData : notnull;

    public Task Sub<TData>(in TypedKey<TData> key, Func<TData, ValueTask> action)
        where TData : notnull;
}