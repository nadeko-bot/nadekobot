namespace NadekoBot.Modules.Searches;

public interface ISearchResultEntry
{
    string Title { get; }
    string Url { get; }
    string DisplayUrl { get; }
    string? Description { get; }
}