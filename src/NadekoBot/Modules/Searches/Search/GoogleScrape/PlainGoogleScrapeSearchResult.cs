namespace NadekoBot.Modules.Searches.GoogleScrape;

public class PlainGoogleScrapeSearchResult : ISearchResult
{
    public required string? Answer { get; init;  } 
    public required IReadOnlyCollection<ISearchResultEntry> Entries { get; init; }
    public required ISearchResultInformation Info { get; init; } 
}