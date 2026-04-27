using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using NadekoBot.AiAgent;
using NadekoBot.Modules.Searches;

namespace NadekoBot.Modules.Utility.AiAgent.Adapters;

public sealed partial class WebAiAdapter(
    ISearchServiceFactory searchFactory,
    IHttpClientFactory httpFactory) : IAiCoreToolGroup, INService
{
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex SpacesOrTabsRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    public string GroupName => "web";
    public string GroupDescription => "Web search with optional page-text fetching for top results.";

    private const int DEFAULT_COUNT = 5;
    private const int MAX_COUNT = 10;
    private const int MAX_READ_PAGES = 3;
    private const int MAX_PAGE_CONTENT_LENGTH = 4000;
    private static readonly TimeSpan _fetchTimeout = TimeSpan.FromSeconds(5);

    private static readonly HtmlParser _parser = new(new()
    {
        IsScripting = false,
        IsEmbedded = false,
        IsSupportingProcessingInstructions = false,
        IsKeepingSourceReferences = false,
        IsNotSupportingFrames = true
    });

    private static readonly string[] _removeSelectors =
    [
        "script", "style", "nav", "header", "footer",
        "noscript", "iframe", "svg", "form", "[role='navigation']",
        "[role='banner']", "[role='contentinfo']"
    ];

    [AiTool(
        "web_search",
        "Search the web for information. Returns titles, URLs, and snippets. "
        + "Optionally fetch full page text from the top results by setting read_pages (max 3). "
        + "Use this when you need current information, facts, documentation, or anything not in your training data.")]
    public async Task<string> WebSearch(
        [AiParam("The search query")]
        string query,
        [AiParam("Number of results to return (default 5, max 10)")]
        int count = DEFAULT_COUNT,
        [AiParam("Number of top result pages to fetch full text content from (default 0, max 3). Pages are fetched in parallel.")]
        int readPages = 0)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw ToolException.InvalidArgument("query is required.");

        query = query.Trim();
        count = Math.Clamp(count, 1, MAX_COUNT);
        readPages = Math.Clamp(readPages, 0, MAX_READ_PAGES);

        var searchService = searchFactory.GetSearchService();
        var data = await searchService.SearchAsync(query);

        if (data is null or { Entries: null or { Count: 0 } })
            return "No search results found.";

        var entries = data.Entries.Take(count).ToList();

        var pageContents = new Dictionary<string, string>();
        if (readPages > 0)
        {
            var toFetch = entries.Take(readPages).ToList();
            var fetchTasks = toFetch.Select(e => FetchPageContentInternalAsync(e.Url)).ToArray();
            var results = await Task.WhenAll(fetchTasks);

            for (var i = 0; i < toFetch.Count; i++)
                pageContents[toFetch[i].Url] = results[i];
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(data.Answer))
            sb.AppendLine($"Answer: {data.Answer}\n");

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            sb.AppendLine($"[{i + 1}] {entry.Title}");
            sb.AppendLine($"URL: {entry.Url}");
            sb.AppendLine($"Snippet: {entry.Description ?? "-"}");

            if (pageContents.TryGetValue(entry.Url, out var content))
            {
                sb.AppendLine("Page content:");
                sb.AppendLine(content);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> FetchPageContentInternalAsync(string url)
    {
        try
        {
            if (!UrlExtensions.IsPublicUrl(url))
                return "(Skipped: non-public URL)";

            using var http = httpFactory.CreateClient();
            http.Timeout = _fetchTimeout;
            http.DefaultRequestHeaders.Clear();
            http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            http.DefaultRequestHeaders.Add("Accept", "text/html");

            using var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"(Failed to fetch: HTTP {(int)response.StatusCode})";

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not null && !contentType.Contains("html", StringComparison.InvariantCultureIgnoreCase))
                return $"(Non-HTML content: {contentType})";

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await _parser.ParseDocumentAsync(stream);

            foreach (var selector in _removeSelectors)
            {
                foreach (var el in document.QuerySelectorAll(selector).ToList())
                    el.Remove();
            }

            var body = document.Body;
            if (body is null)
                return "(No page content found)";

            var text = body.TextContent;
            text = SpacesOrTabsRegex().Replace(text, " ");
            text = MultipleNewlinesRegex().Replace(text, "\n\n");
            text = text.Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "(No readable text content)";

            if (text.Length > MAX_PAGE_CONTENT_LENGTH)
                text = text[..MAX_PAGE_CONTENT_LENGTH] + "...";

            return text;
        }
        catch (TaskCanceledException)
        {
            return "(Fetch timed out)";
        }
        catch (Exception ex)
        {
            return $"(Failed to fetch: {ex.Message})";
        }
    }
}
