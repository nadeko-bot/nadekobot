using OneOf;
using OneOf.Types;

namespace Nadeko.Common;

public static class BotCacheExtensions
{
    public static async ValueTask<T?> GetOrDefaultAsync<T>(this IBotCache cache, TypedKey<T> key)
    {
        var result = await cache.GetAsync(key);
        if (result.TryGetValue(out var val))
            return val;

        return default;
    }
    
    private static TypedKey<byte[]> GetImgKey(Uri uri)
        => new($"image:{uri}");

    public static ValueTask SetImageDataAsync(this IBotCache c, string key, byte[] data)
        => c.SetImageDataAsync(new Uri(key), data);
    public static async ValueTask SetImageDataAsync(this IBotCache c, Uri key, byte[] data)
        => await c.AddAsync(GetImgKey(key), data, expiry: TimeSpan.FromHours(48));

    public static async ValueTask<OneOf<byte[], None>> GetImageDataAsync(this IBotCache c, Uri key)
        => await c.GetAsync(GetImgKey(key));

    public static async Task<TimeSpan?> GetRatelimitAsync(
        this IBotCache c,
        TypedKey<long> key,
        TimeSpan length)
    {
        var now = DateTime.UtcNow;
        var nowB = now.ToBinary();

        var cachedValue = await c.GetOrAddAsync(key,
            () => Task.FromResult(now.ToBinary()),
            expiry: length);

        if (cachedValue == nowB)
            return null;

        var diff = now - DateTime.FromBinary(cachedValue);
        return length - diff;
    }
}