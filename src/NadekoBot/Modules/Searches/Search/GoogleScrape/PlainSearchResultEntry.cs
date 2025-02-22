namespace NadekoBot.Modules.Searches.GoogleScrape;

public sealed class PlainSearchResultEntry : ISearchResultEntry
{
    public string Title { get; init; } = null!;
    public string Url { get; init; } = null!;
    public string DisplayUrl { get; init; } = null!;
    public string? Description { get; init; } = null!;
}