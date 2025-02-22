namespace NadekoBot.Modules.Searches;

public sealed class NasdaqDataResponse<T>
{
    public required T? Data { get; init; }
}