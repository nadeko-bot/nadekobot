using MorseCode.ITask;

namespace NadekoBot.Modules.Searches;

public sealed class GoogleSearchService : SearchServiceBase, INService
{
    private readonly IBotCredsProvider _creds;
    private readonly IHttpClientFactory _httpFactory;

    public GoogleSearchService(IBotCredsProvider creds, IHttpClientFactory httpFactory)
    {
        _creds = creds;
        _httpFactory = httpFactory;
    }
    
    public override async ITask<GoogleImageResult?> SearchImagesAsync(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var creds = _creds.GetCreds();
        var key = creds.Google.ImageSearchId;
        var cx = string.IsNullOrWhiteSpace(key)
            ? "c3f56de3be2034c07"
            : key;
        
        using var http = _httpFactory.CreateClient("google:search");
        http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
        await using var stream = await http.GetStreamAsync(
            $"https://customsearch.googleapis.com/customsearch/v1"
            + $"?cx={cx}"
            + $"&q={Uri.EscapeDataString(query)}"
            + $"&fields=items(image(contextLink%2CthumbnailLink)%2Clink)%2CsearchInformation"
            + $"&key={creds.GoogleApiKey}"
            + $"&searchType=image"
            + $"&safe=active");
        
        var result = await System.Text.Json.JsonSerializer.DeserializeAsync<GoogleImageResult>(stream);

        return result;
    }

    public override async ITask<GoogleCustomSearchResult?> SearchAsync(string? query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var creds = _creds.GetCreds();
        var key = creds.Google.SearchId;
        var cx = string.IsNullOrWhiteSpace(key)
            ? "c7f1dac95987d4571"
            : key;
        
        using var http = _httpFactory.CreateClient("google:search");
        http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
        await using var stream = await http.GetStreamAsync(
            $"https://customsearch.googleapis.com/customsearch/v1"
            + $"?cx={cx}"
            + $"&q={Uri.EscapeDataString(query)}"
            + $"&fields=items(title%2Clink%2CdisplayLink%2Csnippet)%2CsearchInformation"
            + $"&key={creds.GoogleApiKey}"
            + $"&safe=active");
        
        var result = await System.Text.Json.JsonSerializer.DeserializeAsync<GoogleCustomSearchResult>(stream);

        return result;
    }
}