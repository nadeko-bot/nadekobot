using OneOf;
using OneOf.Types;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NadekoBot.Common;

public sealed class RedisBotCache : IBotCache
{
    private static readonly Type[] _supportedTypes =
    [
        typeof(bool), typeof(int), typeof(uint), typeof(long),
        typeof(ulong), typeof(float), typeof(double),
        typeof(string), typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>),
        typeof(RedisValue)
    ];

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        IgnoreReadOnlyProperties = false,
    };
    private readonly ConnectionMultiplexer _conn;

    public RedisBotCache(ConnectionMultiplexer conn)
    {
        _conn = conn;
    }

    public async ValueTask<bool> AddAsync<T>(TypedKey<T> key, T value, TimeSpan? expiry = null, bool overwrite = true)
    {
        // if a null value is passed, remove the key
        if (value is null)
        {
            await RemoveAsync(key);
            return false;
        }
        
        var db = _conn.GetDatabase();
        RedisValue val = IsSupportedType(typeof(T)) 
            ? RedisValue.Unbox(value)
            : JsonSerializer.Serialize(value, _opts);

        var success = await db.StringSetAsync(key.Key,
            val,
            expiry: expiry,
            keepTtl: true,
            when: overwrite ? When.Always : When.NotExists);

        return success;
    }

    public bool IsSupportedType(Type type)
    {
        if (type.IsGenericType)
        {
            var typeDef = type.GetGenericTypeDefinition();
            if (typeDef == typeof(Nullable<>))
                return IsSupportedType(type.GenericTypeArguments[0]);
        }

        foreach (var t in _supportedTypes)
        {
            if (type == t)
                return true;
        }

        return false;
    }
    
    public async ValueTask<OneOf<T, None>> GetAsync<T>(TypedKey<T> key)
    {
        var db = _conn.GetDatabase();
        var val = await db.StringGetAsync(key.Key);
        if (val == default)
            return new None();

        if (IsSupportedType(typeof(T)))
            return (T)((IConvertible)val).ToType(typeof(T), null);

        return JsonSerializer.Deserialize<T>(val.ToString(), _opts)!;
    }

    public async ValueTask<bool> RemoveAsync<T>(TypedKey<T> key)
    {
        var db = _conn.GetDatabase();

        return await db.KeyDeleteAsync(key.Key);
    }

    public async ValueTask<T?> GetOrAddAsync<T>(TypedKey<T> key, Func<Task<T?>> createFactory, TimeSpan? expiry = null)
    {
        var result = await GetAsync(key);

        return await result.Match<Task<T?>>(
            v => Task.FromResult<T?>(v),
            async _ =>
            {
                var factoryValue = await createFactory();

                if (factoryValue is null)
                    return default;
                
                await AddAsync(key, factoryValue, expiry);
                
                // get again to make sure it's the cached value
                // and not the late factory value, in case there's a race condition
                
                var newResult = await GetAsync(key);

                // it's fine to do this, it should blow up if something went wrong.
                return newResult.Match<T?>(
                    v => v,
                    _ => default);
            });
    }
}