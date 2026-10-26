namespace NadekoBot.Modules.Music;

// Builds at most one value for each key, also when many callers ask for the same key at the same time.
public static class OncePerKey
{
    public static async Task<(TValue Value, bool Created)> GetOrCreateAsync<TValue, TState>(
        ConcurrentDictionary<ulong, TValue> values,
        ConcurrentDictionary<ulong, SemaphoreSlim> locks,
        ulong key,
        TState state,
        Func<TState, Task<TValue>> create)
    {
        if (values.TryGetValue(key, out var existing))
            return (existing, false);

        var gate = locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (values.TryGetValue(key, out existing))
                return (existing, false);

            var created = await create(state);
            values[key] = created;
            return (created, true);
        }
        finally
        {
            gate.Release();
        }
    }
}
