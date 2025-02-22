namespace Nadeko.Common;

public delegate TOut PipeFunc<TIn, out TOut>(in TIn a);
public delegate TOut PipeFunc<TIn1, TIn2, out TOut>(in TIn1 a, in TIn2 b);

public static class PipeExtensions
{
    public static TOut Pipe<TIn, TOut>(this TIn a, Func<TIn, TOut> fn)
        => fn(a);
    
    public static TOut Pipe<TIn, TOut>(this TIn a, PipeFunc<TIn, TOut> fn)
        => fn(a);
    
    public static TOut Pipe<TIn1, TIn2, TOut>(this (TIn1, TIn2) a, PipeFunc<TIn1, TIn2, TOut> fn)
        => fn(a.Item1, a.Item2);

    public static (TIn, TExtra) With<TIn, TExtra>(this TIn a, TExtra b)
        => (a, b);
    
    public static async Task<TOut> Pipe<TIn, TOut>(this Task<TIn> a, Func<TIn, TOut> fn)
        => fn(await a);
}