namespace NadekoBot.Medusa;

public readonly struct ParseResult<T>
{
    /// <summary>
    /// Whether the parsing was successful
    /// </summary>
    public bool IsSuccess { get; private init; }
    
    /// <summary>
    /// Parsed value. It should only have value if <see cref="IsSuccess"/> is set to true
    /// </summary>
    public T? Data { get; private init;  }

    /// <summary>
    /// Instantiate a **successful** parse result
    /// </summary>
    /// <param name="data">Parsed value</param>
    public ParseResult(T data)
    {
        Data = data;
        IsSuccess = true;
    }
    

    /// <summary>
    /// Create a new <see cref="ParseResult{T}"/> with IsSuccess = false
    /// </summary>
    /// <returns>A new <see cref="ParseResult{T}"/></returns>
    public static ParseResult<T> Fail()
        => new ParseResult<T>
        {
            IsSuccess = false,
            Data = default,
        };

    /// <summary>
    /// Create a new <see cref="ParseResult{T}"/> with IsSuccess = true
    /// </summary>
    /// <param name="obj">Value of the parsed object</param>
    /// <returns>A new <see cref="ParseResult{T}"/></returns>
    public static ParseResult<T> Success(T obj)
        => new ParseResult<T>
        {
            IsSuccess = true,
            Data = obj,
        };
}