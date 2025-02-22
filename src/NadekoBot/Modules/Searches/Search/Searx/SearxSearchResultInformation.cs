namespace NadekoBot.Modules.Searches;

public sealed class SearxSearchResultInformation : ISearchResultInformation
{
    public string TotalResults { get; init; } = string.Empty;
    public string SearchTime { get; init; } = string.Empty;
}