using System.Security.Cryptography;

namespace Nadeko.Common;

public static class EnumerableExtensions
{
    /// <summary>
    ///     Concatenates the members of a collection, using the specified separator between each member.
    /// </summary>
    /// <param name="data">Collection to join</param>
    /// <param name="separator">
    ///     The character to use as a separator. separator is included in the returned string only if
    ///     values has more than one element.
    /// </param>
    /// <param name="func">Optional transformation to apply to each element before concatenation.</param>
    /// <typeparam name="T">The type of the members of values.</typeparam>
    /// <returns>
    ///     A string that consists of the members of values delimited by the separator character. -or- Empty if values has
    ///     no elements.
    /// </returns>
    public static string Join<T>(this IEnumerable<T> data, char separator, Func<T, string>? func = null)
        => string.Join(separator, data.Select(func ?? (x => x?.ToString() ?? string.Empty)));

    /// <summary>
    ///     Concatenates the members of a collection, using the specified separator between each member.
    /// </summary>
    /// <param name="data">Collection to join</param>
    /// <param name="separator">
    ///     The string to use as a separator.separator is included in the returned string only if values
    ///     has more than one element.
    /// </param>
    /// <param name="func">Optional transformation to apply to each element before concatenation.</param>
    /// <typeparam name="T">The type of the members of values.</typeparam>
    /// <returns>
    ///     A string that consists of the members of values delimited by the separator character. -or- Empty if values has
    ///     no elements.
    /// </returns>
    public static string Join<T>(this IEnumerable<T> data, string separator, Func<T, string>? func = null)
        => string.Join(separator, data.Select(func ?? (x => x?.ToString() ?? string.Empty)));
    
    /// <summary>
    ///     Randomize element order by performing the Fisher-Yates shuffle
    /// </summary>
    /// <typeparam name="T">Item type</typeparam>
    /// <param name="items">Items to shuffle</param>
    public static IReadOnlyList<T> Shuffle<T>(this IEnumerable<T> items)
    {
        var list = items.ToArray();
        var n = list.Length;
        while (n-- > 1)
        {
            var k = RandomNumberGenerator.GetInt32(n);
            (list[k], list[n]) = (list[n], list[k]);
        }

        return list;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcurrentDictionary{TKey,TValue}" /> class
    ///     that contains elements copied from the specified <see cref="IEnumerable{T}" />
    ///     has the default concurrency level, has the default initial capacity,
    ///     and uses the default comparer for the key type.
    /// </summary>
    /// <param name="dict">
    ///     The <see cref="IEnumerable{T}" /> whose elements are copied to the new
    ///     <see cref="ConcurrentDictionary{TKey,TValue}" />.
    /// </param>
    /// <returns>A new instance of the <see cref="ConcurrentDictionary{TKey,TValue}" /> class</returns>
    public static ConcurrentDictionary<TKey, TValue> ToConcurrent<TKey, TValue>(
        this IEnumerable<KeyValuePair<TKey, TValue>> dict)
        where TKey : notnull
        => new(dict);
    
    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcurrentDictionary{TKey,TValue}" /> class
    ///     that contains elements copied from the specified <see cref="IEnumerable{T}" />
    ///     has the default concurrency level, has the default initial capacity,
    ///     and uses the default comparer for the key type.
    /// </summary>
    /// <param name="dict">
    ///     The <see cref="IEnumerable{T}" /> whose elements are copied to the new
    ///     <see cref="ConcurrentDictionary{TKey,TValue}" />.
    /// </param>
    /// <returns>A new instance of the <see cref="ConcurrentDictionary{TKey,TValue}" /> class</returns>
    public static ConcurrentHashSet<TValue> ToConcurrentSet<TValue>(
        this IReadOnlyCollection<TValue> dict)
        where TValue : notnull
        => new(dict);

    public static IndexedCollection<T> ToIndexed<T>(this IEnumerable<T> enumerable)
        where T : class, IIndexed
        => new(enumerable);

    /// <summary>
    ///     Creates a task that will complete when all of the <see cref="Task{TResult}" /> objects in an enumerable
    ///     collection have completed
    /// </summary>
    /// <param name="tasks">The tasks to wait on for completion.</param>
    /// <typeparam name="TResult">The type of the completed task.</typeparam>
    /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
    public static Task<TResult[]> WhenAll<TResult>(this IEnumerable<Task<TResult>> tasks)
        => Task.WhenAll(tasks);

    /// <summary>
    ///     Creates a task that will complete when all of the <see cref="Task" /> objects in an enumerable
    ///     collection have completed
    /// </summary>
    /// <param name="tasks">The tasks to wait on for completion.</param>
    /// <returns>A task that represents the completion of all of the supplied tasks.</returns>
    public static Task WhenAll(this IEnumerable<Task> tasks)
        => Task.WhenAll(tasks);
}