namespace NadekoBot.Modules.Searches.DuckDuckGo;

public sealed class DdgSearchResult : ISearchResult
{
    public required string? Answer { get; init; }
    public required IReadOnlyCollection<ISearchResultEntry> Entries { get; init; }
    public required ISearchResultInformation Info { get; init; }
}

public sealed class DdgSearchResultEntry : ISearchResultEntry
{
    public string Title { get; init; } = null!;
    public string Url { get; init; } = null!;
    public string DisplayUrl => Url;
    public string? Description { get; init; }
}

public sealed class DdgSearchResultInfo : ISearchResultInformation
{
    public string TotalResults { get; init; } = null!;
    public string SearchTime { get; init; } = null!;
}
