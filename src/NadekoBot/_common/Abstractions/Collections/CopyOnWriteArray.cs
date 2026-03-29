using System.Runtime.CompilerServices;

namespace System.Collections.Generic;

public struct CopyOnWriteArray<T> where T : class
{
    private volatile T[] _items;

    public CopyOnWriteArray()
        => _items = [];

    public CopyOnWriteArray(T[] initial)
        => _items = initial;

    public readonly T[] Items
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items;
    }

    public readonly int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items.Length;
    }

    public void Replace(T[] items)
        => _items = items;

    public void Add(in T item)
    {
        while (true)
        {
            var snap = _items;
            var arr = new T[snap.Length + 1];
            Array.Copy(snap, arr, snap.Length);
            arr[snap.Length] = item;
            if (Interlocked.CompareExchange(ref _items, arr, snap) == snap)
                return;
        }
    }

    public T? Remove(int key, Func<T, int> keySelector)
    {
        while (true)
        {
            var snap = _items;
            var idx = -1;
            for (var i = 0; i < snap.Length; i++)
            {
                if (keySelector(snap[i]) == key)
                {
                    idx = i;
                    break;
                }
            }

            if (idx == -1)
                return null;

            var removed = snap[idx];
            var arr = new T[snap.Length - 1];
            Array.Copy(snap, arr, idx);
            Array.Copy(snap, idx + 1, arr, idx, snap.Length - idx - 1);
            if (Interlocked.CompareExchange(ref _items, arr, snap) == snap)
                return removed;
        }
    }

    public bool Update(int key, in T replacement, Func<T, int> keySelector)
    {
        while (true)
        {
            var snap = _items;
            var idx = -1;
            for (var i = 0; i < snap.Length; i++)
            {
                if (keySelector(snap[i]) == key)
                {
                    idx = i;
                    break;
                }
            }

            if (idx == -1)
                return false;

            var arr = new T[snap.Length];
            Array.Copy(snap, arr, snap.Length);
            arr[idx] = replacement;
            if (Interlocked.CompareExchange(ref _items, arr, snap) == snap)
                return true;
        }
    }

    public void Upsert(int key, in T item, Func<T, int> keySelector)
    {
        if (!Update(key, in item, keySelector))
            Add(in item);
    }
}
