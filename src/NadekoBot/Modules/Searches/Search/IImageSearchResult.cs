namespace NadekoBot.Modules.Searches;

public interface IImageSearchResult
{
    ISearchResultInformation Info { get; }
    
    IReadOnlyCollection<IImageSearchResultEntry> Entries { get; }
}

public interface IImageSearchResultEntry
{
    string Link { get; }
}