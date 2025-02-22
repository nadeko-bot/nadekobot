#nullable disable
namespace NadekoBot.Modules.Searches.Common;

public sealed class GoogleSearchResult
{
    public string Title { get; }
    public string Link { get; }
    public string Text { get; }

    public GoogleSearchResult(string title, string link, string text)
    {
        Title = title;
        Link = link;
        Text = text;
    }
}