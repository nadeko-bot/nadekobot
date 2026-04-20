using System.Runtime.CompilerServices;

namespace NadekoBot.Modules.Xp.Services;

public sealed class XpCooldownMap
{
    private readonly ConcurrentDictionary<ulong, long> _cooldowns = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAddCooldown(ulong userId, float cdInMinutes)
    {
        if (cdInMinutes <= float.Epsilon)
            return true;

        var now = Environment.TickCount64;
        var next = now + (long)(cdInMinutes * 60_000);

        while (true)
        {
            if (!_cooldowns.TryGetValue(userId, out var existing))
            {
                if (_cooldowns.TryAdd(userId, next))
                    return true;

                continue;
            }

            if (existing > now)
                return false;

            if (_cooldowns.TryUpdate(userId, next, existing))
                return true;
        }
    }

    public void Cleanup()
    {
        if (_cooldowns.IsEmpty)
            return;

        var now = Environment.TickCount64;
        foreach (var kv in _cooldowns)
        {
            if (kv.Value <= now)
                _cooldowns.TryRemove(new(kv.Key, kv.Value));
        }
    }
}
