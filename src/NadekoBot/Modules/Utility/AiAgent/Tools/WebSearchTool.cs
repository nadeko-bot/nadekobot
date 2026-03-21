using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using NadekoBot.Modules.Searches;

namespace NadekoBot.Modules.Utility.AiAgent.Tools;

/// <summary>
/// Searches the web using the bot's configured search engine and optionally
/// fetches full page content from the top results.
/// </summary>
public sealed class WebSearchTool(
    ISearchServiceFactory searchFactory,
    IHttpClientFactory httpFactory) : IAiTool, INService
{
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

    public string Name => "web_search";

    public string Description =>
        "Search the web for information. Returns titles, URLs, and snippets. " +
        "Optionally fetch full page text from the top results by setting read_pages (max 3). " +
        "Use this when you need current information, facts, documentation, or anything not in your training data.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse($$"""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "The search query"
                },
                "count": {
                    "type": "integer",
                    "description": "Number of results to return (default {{DEFAULT_COUNT}}, max {{MAX_COUNT}})"
                },
                "read_pages": {
                    "type": "integer",
                    "description": "Number of top result pages to fetch full text content from (default 0, max {{MAX_READ_PAGES}}). Pages are fetched in parallel."
                }
            },
            "required": ["query"]
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(AiToolContext context, JsonElement arguments)
    {
        if (!arguments.TryGetProperty("query", out var queryEl)
            || string.IsNullOrWhiteSpace(queryEl.GetString()))
            return "Error: query is required.";

        var query = queryEl.GetString()!.Trim();

        var count = DEFAULT_COUNT;
        if (arguments.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var c))
            count = Math.Clamp(c, 1, MAX_COUNT);

        var readPages = 0;
        if (arguments.TryGetProperty("read_pages", out var rpEl) && rpEl.TryGetInt32(out var rp))
            readPages = Math.Clamp(rp, 0, MAX_READ_PAGES);

        var searchService = searchFactory.GetSearchService();
        var data = await searchService.SearchAsync(query);

        if (data is null or { Entries: null or { Count: 0 } })
            return "No search results found.";

        var entries = data.Entries.Take(count).ToList();

        // Fetch page content in parallel if requested
        var pageContents = new Dictionary<string, string>();
        if (readPages > 0)
        {
            var toFetch = entries.Take(readPages).ToList();
            var fetchTasks = toFetch.Select(e => FetchPageContentAsync(e.Url)).ToArray();
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

    /// <summary>
    /// Fetches a URL and extracts visible text content using AngleSharp.
    /// </summary>
    private async Task<string> FetchPageContentAsync(string url)
    {
        try
        {
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
            if (contentType is not null && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
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

            // Collapse whitespace
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
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
