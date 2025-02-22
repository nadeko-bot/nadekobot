using System.Collections;

namespace Nadeko.Common;

public interface IIndexed
{
    int Index { get; set; }
}

public class IndexedCollection<T> : IList<T>
    where T : class, IIndexed
{
    public List<T> Source { get; }

    public int Count
        => Source.Count;

    public bool IsReadOnly
        => false;

    public virtual T this[int index]
    {
        get => Source[index];
        set
        {
            lock (_locker)
            {
                value.Index = index;
                Source[index] = value;
            }
        }
    }

    private readonly object _locker = new();

    public IndexedCollection()
        => Source = new();

    public IndexedCollection(IEnumerable<T> source)
    {
        lock (_locker)
        {
            Source = source.OrderBy(x => x.Index).ToList();
            UpdateIndexes();
        }
    }

    public int IndexOf(T item)
        => item?.Index ?? -1;

    public IEnumerator<T> GetEnumerator()
        => Source.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => Source.GetEnumerator();

    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_locker)
        {
            item.Index = Source.Count;
            Source.Add(item);
        }
    }

    public virtual void Clear()
    {
        lock (_locker)
        {
            Source.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_locker)
        {
            return Source.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_locker)
        {
            Source.CopyTo(array, arrayIndex);
        }
    }

    public virtual bool Remove(T item)
    {
        lock (_locker)
        {
            if (Source.Remove(item))
            {
                for (var i = 0; i < Source.Count; i++)
                {
                    if (Source[i].Index != i)
                        Source[i].Index = i;
                }

                return true;
            }
        }

        return false;
    }

    public virtual void Insert(int index, T item)
    {
        lock (_locker)
        {
            Source.Insert(index, item);
            for (var i = index; i < Source.Count; i++)
                Source[i].Index = i;
        }
    }

    public virtual void RemoveAt(int index)
    {
        lock (_locker)
        {
            Source.RemoveAt(index);
            for (var i = index; i < Source.Count; i++)
                Source[i].Index = i;
        }
    }

    public void UpdateIndexes()
    {
        lock (_locker)
        {
            for (var i = 0; i < Source.Count; i++)
            {
                if (Source[i].Index != i)
                    Source[i].Index = i;
            }
        }
    }

    public static implicit operator List<T>(IndexedCollection<T> x)
        => x.Source;

    public List<T> ToList()
        => Source.ToList();
}