using StackExchange.Redis;

namespace NadekoBot.Common;

public sealed class RedisPubSub : IPubSub
{
    private readonly IBotCreds _creds;
    private readonly ConnectionMultiplexer _multi;
    private readonly ISeria _serializer;

    public RedisPubSub(ConnectionMultiplexer multi, ISeria serializer, IBotCreds creds)
    {
        _multi = multi;
        _serializer = serializer;
        _creds = creds;
    }

    public Task Pub<TData>(in TypedKey<TData> key, TData data)
        where TData : notnull
    {
        var serialized = _serializer.Serialize(data);
        return _multi.GetSubscriber()
                     .PublishAsync(new RedisChannel($"{_creds.RedisKey()}:{key.Key}", RedisChannel.PatternMode.Literal),
                         serialized,
                         CommandFlags.FireAndForget);
    }

    public Task Sub<TData>(in TypedKey<TData> key, Func<TData, ValueTask> action)
        where TData : notnull
    {
        var eventName = key.Key;

        async void OnSubscribeHandler(RedisChannel _, RedisValue data)
        {
            try
            {
                var dataObj = _serializer.Deserialize<TData>(data);
                if (dataObj is not null)
                    await action(dataObj);
                else
                {
                    Log.Warning("Publishing event {EventName} with a null value. This is not allowed",
                        eventName);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error handling the event {EventName}: {ErrorMessage}", eventName, ex.Message);
            }
        }

        return _multi.GetSubscriber()
                     .SubscribeAsync(
                         new RedisChannel($"{_creds.RedisKey()}:{eventName}", RedisChannel.PatternMode.Literal),
                         OnSubscribeHandler);
    }
}