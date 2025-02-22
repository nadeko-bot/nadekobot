using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using MorseCode.ITask;

namespace NadekoBot.Modules.Searches.GoogleScrape;

public sealed class GoogleScrapeService : SearchServiceBase, INService
{
    private static readonly HtmlParser _googleParser = new(new()
    {
        IsScripting = false,
        IsEmbedded = false,
        IsSupportingProcessingInstructions = false,
        IsKeepingSourceReferences = false,
        IsNotSupportingFrames = true
    });

    
    private readonly IHttpClientFactory _httpFactory;

    public GoogleScrapeService(IHttpClientFactory httpClientFactory)
        => _httpFactory = httpClientFactory;

    public override async ITask<ISearchResult?> SearchAsync(string? query)
    {
        ArgumentNullException.ThrowIfNull(query);
        
        query = Uri.EscapeDataString(query)?.Replace(' ', '+');

        var fullQueryLink = $"https://www.google.ca/search?q={query}&safe=on&lr=lang_eng&hl=en&ie=utf-8&oe=utf-8";

        using var msg = new HttpRequestMessage(HttpMethod.Get, fullQueryLink);
        msg.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.77 Safari/537.36");
        msg.Headers.Add("Cookie", "CONSENT=YES+shp.gws-20210601-0-RC2.en+FX+423;");

        using var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Clear();

        using var response = await http.SendAsync(msg);
        await using var content = await response.Content.ReadAsStreamAsync();

        using var document = await _googleParser.ParseDocumentAsync(content);
        var elems = document.QuerySelectorAll("div.g, div.mnr-c > div > div");

        var resultsElem = document.QuerySelector("#result-stats");
        var resultsArr = resultsElem?.TextContent.Split("results");
        var totalResults = resultsArr?.Length is null or 0
            ? null
            : resultsArr[0];

        var time = resultsArr is null or {Length: < 2}
            ? null
            : resultsArr[1]
              .Replace("(", string.Empty)
              .Replace("seconds)", string.Empty);
        
        //var time = resultsElem.Children.FirstOrDefault()?.TextContent
        //^ this doesn't work for some reason, <nobr> is completely missing in parsed collection
        if (!elems.Any())
            return default;

        var results = elems.Select(elem =>
                           {
                               var aTag = elem.QuerySelector("a");

                               if (aTag is null)
                                   return null;

                               var url = ((IHtmlAnchorElement)aTag).Href;
                               var title = aTag.QuerySelector("h3")?.TextContent;

                               var txt = aTag.ParentElement
                                             ?.NextElementSibling
                                             ?.QuerySelector("span")
                                             ?.TextContent
                                             .StripHtml()
                                         ?? elem
                                            ?.QuerySelectorAll("span")
                                            .Skip(3)
                                            .FirstOrDefault()
                                            ?.TextContent
                                            .StripHtml();
                                             // .Select(x => x.TextContent.StripHtml())
                                             // .Join("\n");

                               if (string.IsNullOrWhiteSpace(url)
                                   || string.IsNullOrWhiteSpace(title)
                                   || string.IsNullOrWhiteSpace(txt))
                                   return null;

                               return new PlainSearchResultEntry
                               {
                                   Title = title,
                                   Url = url,
                                   DisplayUrl = url,
                                   Description = txt
                               };
                           })
                           .Where(x => x is not null)
                           .ToList();

        // return new GoogleSearchResult(results.AsReadOnly(), fullQueryLink, totalResults);

        return new PlainGoogleScrapeSearchResult()
        {
            Answer = null,
            Entries = results!,
            Info = new PlainSearchResultInfo()
            {
                SearchTime = time ?? "?",
                TotalResults = totalResults ?? "?"
            }
        };
    }

    
    // someone can mr this
    public override ITask<IImageSearchResult?> SearchImagesAsync(string query)
        => throw new NotSupportedException();
}