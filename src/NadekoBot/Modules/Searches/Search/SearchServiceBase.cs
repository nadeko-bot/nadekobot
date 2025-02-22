using MorseCode.ITask;

namespace NadekoBot.Modules.Searches;

public abstract class SearchServiceBase : ISearchService
{
    public abstract ITask<ISearchResult?> SearchAsync(string? query);
    public abstract ITask<IImageSearchResult?> SearchImagesAsync(string query);
}