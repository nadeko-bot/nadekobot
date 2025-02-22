namespace NadekoBot.Modules.Searches;

public interface ISearchResult
{
    string? Answer { get; }
    IReadOnlyCollection<ISearchResultEntry> Entries { get; }
    ISearchResultInformation Info { get; }
}