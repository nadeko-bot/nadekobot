using System.Security.Cryptography;

namespace Nadeko.Common;

// made for expressions because they almost never get added
// and they get looped through constantly
public static class ArrayExtensions
{
    /// <summary>
    ///     Create a new array from the old array + new element at the end
    /// </summary>
    /// <param name="input">Input array</param>
    /// <param name="added">Item to add to the end of the output array</param>
    /// <typeparam name="T">Type of the array</typeparam>
    /// <returns>A new array with the new element at the end</returns>
    public static T[] With<T>(this T[] input, T added)
    {
        var newExprs = new T[input.Length + 1];
        Array.Copy(input, 0, newExprs, 0, input.Length);
        newExprs[input.Length] = added;
        return newExprs;
    }

    /// <summary>
    ///     Creates a new array by applying the specified function to every element in the input array
    /// </summary>
    /// <param name="arr">Array to modify</param>
    /// <param name="f">Function to apply</param>
    /// <typeparam name="TIn">Orignal type of the elements in the array</typeparam>
    /// <typeparam name="TOut">Output type of the elements of the array</typeparam>
    /// <returns>New array with updated elements</returns>
    public static TOut[] Map<TIn, TOut>(this TIn[] arr, Func<TIn, TOut> f)
        => Array.ConvertAll(arr, x => f(x));

    /// <summary>
    ///     Creates a new array by applying the specified function to every element in the input array
    /// </summary>
    /// <param name="col">Array to modify</param>
    /// <param name="f">Function to apply</param>
    /// <typeparam name="TIn">Orignal type of the elements in the array</typeparam>
    /// <typeparam name="TOut">Output type of the elements of the array</typeparam>
    /// <returns>New array with updated elements</returns>
    public static TOut[] Map<TIn, TOut>(this IReadOnlyCollection<TIn> col, Func<TIn, TOut> f)
    {
        var toReturn = new TOut[col.Count];
        
        var i = 0;
        foreach (var item in col)
            toReturn[i++] = f(item);

        return toReturn;
    }

    public static T? RandomOrDefault<T>(this T[] data)
    {
        if (data.Length == 0)
            return default;

        var index = RandomNumberGenerator.GetInt32(0, data.Length);
        return data[index];
    }
}