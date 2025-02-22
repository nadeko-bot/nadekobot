using OneOf;
using OneOf.Types;

namespace Nadeko.Common;

public interface IBotCache
{
    /// <summary>
    /// Adds an item to the cache
    /// </summary>
    /// <param name="key">Key to add</param>
    /// <param name="value">Value to add to the cache</param>
    /// <param name="expiry">Optional expiry</param>
    /// <param name="overwrite">Whether old value should be overwritten</param>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <returns>Returns whether add was sucessful. Always true unless ovewrite = false</returns>
    ValueTask<bool> AddAsync<T>(TypedKey<T> key, T value, TimeSpan? expiry = null, bool overwrite = true);
    
    /// <summary>
    /// Get an element from the cache
    /// </summary>
    /// <param name="key">Key</param>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <returns>Either a value or <see cref="None"/></returns>
    ValueTask<OneOf<T, None>> GetAsync<T>(TypedKey<T> key);
    
    /// <summary>
    /// Remove a key from the cache
    /// </summary>
    /// <param name="key">Key to remove</param>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <returns>Whether there was item</returns>
    ValueTask<bool> RemoveAsync<T>(TypedKey<T> key);

    /// <summary>
    /// Get the key if it exists or add a new one
    /// </summary>
    /// <param name="key">Key to get and potentially add</param>
    /// <param name="createFactory">Value creation factory</param>
    /// <param name="expiry">Optional expiry</param>
    /// <typeparam name="T">Type of the value</typeparam>
    /// <returns>The retrieved or newly added value</returns>
    ValueTask<T?> GetOrAddAsync<T>(
        TypedKey<T> key,
        Func<Task<T?>> createFactory,
        TimeSpan? expiry = null);
}