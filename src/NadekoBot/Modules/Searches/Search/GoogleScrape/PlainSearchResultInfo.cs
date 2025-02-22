namespace NadekoBot.Modules.Searches.GoogleScrape;

public sealed class PlainSearchResultInfo : ISearchResultInformation
{
    public string TotalResults { get; init; } = null!;
    public string SearchTime { get; init; } = null!;
}