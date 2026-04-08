using System.Diagnostics;
using System.Globalization;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using MorseCode.ITask;

namespace NadekoBot.Modules.Searches.DuckDuckGo;

public sealed class DuckDuckGoScrapeService : SearchServiceBase, INService
{
    private static readonly HtmlParser _parser = new(new()
    {
        IsScripting = false,
        IsEmbedded = false,
        IsSupportingProcessingInstructions = false,
        IsKeepingSourceReferences = false,
        IsNotSupportingFrames = true
    });

    private readonly IHttpClientFactory _httpFactory;

    public DuckDuckGoScrapeService(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    public override async ITask<DdgSearchResult?> SearchAsync(string? query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var startTime = Stopwatch.GetTimestamp();

        using var http = _httpFactory.CreateClient();
        using var msg = new HttpRequestMessage(HttpMethod.Get,
            $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}&kp=1");

        msg.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        using var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync();
        using var document = await _parser.ParseDocumentAsync(content);

        var elapsed = Stopwatch.GetElapsedTime(startTime);

        var resultLinks = document.QuerySelectorAll(".result__a");
        var resultSnippets = document.QuerySelectorAll(".result__snippet");

        var entries = new List<DdgSearchResultEntry>();

        for (var i = 0; i < resultLinks.Length; i++)
        {
            var anchor = resultLinks[i] as IHtmlAnchorElement;
            if (anchor is null)
                continue;

            var href = anchor.Href;
            var title = anchor.TextContent?.Trim();

            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(title))
                continue;

            // DDG wraps links through a redirect; extract actual URL
            if (href.Contains("duckduckgo.com/l/", StringComparison.InvariantCultureIgnoreCase))
            {
                var uddgIdx = href.IndexOf("uddg=", StringComparison.InvariantCultureIgnoreCase);
                if (uddgIdx >= 0)
                    href = Uri.UnescapeDataString(href[(uddgIdx + 5)..].Split('&')[0]);
            }

            var snippet = i < resultSnippets.Length
                ? resultSnippets[i].TextContent?.Trim()
                : null;

            entries.Add(new DdgSearchResultEntry
            {
                Title = title,
                Url = href,
                Description = snippet
            });
        }

        if (entries.Count == 0)
            return null;

        return new DdgSearchResult
        {
            Answer = null,
            Entries = entries,
            Info = new DdgSearchResultInfo
            {
                TotalResults = entries.Count.ToString(CultureInfo.InvariantCulture),
                SearchTime = elapsed.TotalSeconds.ToString("N2", CultureInfo.InvariantCulture)
            }
        };
    }

    public override async ITask<DdgImageSearchResult?> SearchImagesAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var startTime = Stopwatch.GetTimestamp();

        using var http = _httpFactory.CreateClient();
        using var msg = new HttpRequestMessage(HttpMethod.Get,
            $"https://duckduckgo.com/?q={Uri.EscapeDataString(query)}&iar=images&iax=images&ia=images&kp=1");

        msg.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        using var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        var elapsed = Stopwatch.GetElapsedTime(startTime);

        // DDG image results are loaded via JS with vqd token. 
        // Extract vqd from the HTML and call the image API endpoint.
        var vqdIndex = html.IndexOf("vqd=\"", StringComparison.InvariantCultureIgnoreCase);
        if (vqdIndex < 0)
            vqdIndex = html.IndexOf("vqd=", StringComparison.InvariantCultureIgnoreCase);

        if (vqdIndex < 0)
            return null;

        var vqdStart = html.IndexOf('"', vqdIndex) + 1;
        if (vqdStart <= 0)
        {
            // Try vqd= without quotes (in query param style)
            vqdStart = vqdIndex + 4;
        }

        var vqdEnd = html.IndexOf('"', vqdStart);
        if (vqdEnd < 0)
            vqdEnd = html.IndexOf('&', vqdStart);
        if (vqdEnd < 0)
            vqdEnd = html.IndexOf('\'', vqdStart);
        if (vqdEnd < 0)
            return null;

        var vqd = html[vqdStart..vqdEnd];

        if (string.IsNullOrWhiteSpace(vqd))
            return null;

        using var imgMsg = new HttpRequestMessage(HttpMethod.Get,
            $"https://duckduckgo.com/i.js?l=us-en&o=json&q={Uri.EscapeDataString(query)}&vqd={vqd}&p=1");

        imgMsg.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        using var imgResponse = await http.SendAsync(imgMsg);

        if (!imgResponse.IsSuccessStatusCode)
            return null;

        await using var imgStream = await imgResponse.Content.ReadAsStreamAsync();
        var imgResult = await System.Text.Json.JsonSerializer.DeserializeAsync<DdgImageApiResponse>(imgStream);

        if (imgResult?.Results is null or { Count: 0 })
            return null;

        return new DdgImageSearchResult
        {
            Entries = imgResult.Results,
            Info = new DdgSearchResultInfo
            {
                TotalResults = imgResult.Results.Count.ToString(CultureInfo.InvariantCulture),
                SearchTime = Stopwatch.GetElapsedTime(startTime).TotalSeconds.ToString("N2", CultureInfo.InvariantCulture)
            }
        };
    }
}
