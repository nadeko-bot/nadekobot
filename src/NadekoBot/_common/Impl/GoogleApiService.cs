#nullable disable
using Google;
using Google.Apis.Services;
using Google.Apis.Urlshortener.v1;
using Google.Apis.YouTube.v3;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;

namespace NadekoBot.Services;

public sealed partial class GoogleApiService : IGoogleApiService, INService
{
    private static readonly Regex
        _plRegex = new(@"(?:youtu\.be\/|list=)(?<id>[\da-zA-Z\-_]*)", RegexOptions.Compiled);


    private readonly YouTubeService _yt;
    private readonly UrlshortenerService _sh;

    //private readonly Regex YtVideoIdRegex = new Regex(@"(?:youtube\.com\/\S*(?:(?:\/e(?:mbed))?\/|watch\?(?:\S*?&?v\=))|youtu\.be\/)(?<id>[a-zA-Z0-9_-]{6,11})", RegexOptions.Compiled);
    private readonly IBotCredsProvider _creds;
    private readonly IHttpClientFactory _httpFactory;

    public GoogleApiService(IBotCredsProvider creds, IHttpClientFactory factory) : this()
    {
        _creds = creds;
        _httpFactory = factory;

        var bcs = new BaseClientService.Initializer
        {
            ApplicationName = "Nadeko Bot",
            ApiKey = _creds.GetCreds().GoogleApiKey
        };

        _yt = new(bcs);
        _sh = new(bcs);
    }

    public async Task<IEnumerable<string>> GetPlaylistIdsByKeywordsAsync(string keywords, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            throw new ArgumentNullException(nameof(keywords));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var match = _plRegex.Match(keywords);
        if (match.Length > 1)
            return new[] { match.Groups["id"].Value };
        var query = _yt.Search.List("snippet");
        query.MaxResults = count;
        query.Type = "playlist";
        query.Q = keywords;

        return (await query.ExecuteAsync()).Items.Select(i => i.Id.PlaylistId);
    }

    public async Task<IEnumerable<string>> GetRelatedVideosAsync(string id, int count = 2, string user = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentNullException(nameof(id));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var query = _yt.Search.List("snippet");
        query.MaxResults = count;
        query.Q = id;
        // query.RelatedToVideoId = id;
        query.Type = "video";
        query.QuotaUser = user;
        // bad workaround as there's no replacement for related video querying right now.
        // Query youtube with the id of the video, take a second video in the results
        // skip the first one as that's probably the same video.
        return (await query.ExecuteAsync()).Items.Select(i => "https://www.youtube.com/watch?v=" + i.Id.VideoId).Skip(1);
    }

    public async Task<IReadOnlyList<string>> GetVideoLinksByKeywordAsync(string keywords, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            throw new ArgumentNullException(nameof(keywords));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var query = _yt.Search.List("snippet");
        query.MaxResults = count;
        query.Q = keywords;
        query.Type = "video";
        query.SafeSearch = SearchResource.ListRequest.SafeSearchEnum.Strict;
        return (await query.ExecuteAsync()).Items.Select(i => "https://www.youtube.com/watch?v=" + i.Id.VideoId).ToArray();
    }

    public async Task<IEnumerable<(string Name, string Id, string Url, string Thumbnail)>> GetVideoInfosByKeywordAsync(
        string keywords,
        int count = 1)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            throw new ArgumentNullException(nameof(keywords));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var query = _yt.Search.List("snippet");
        query.MaxResults = count;
        query.Q = keywords;
        query.Type = "video";
        return (await query.ExecuteAsync()).Items.Select(i
            => (i.Snippet.Title.TrimTo(50),
                    i.Id.VideoId,
                    "https://www.youtube.com/watch?v=" + i.Id.VideoId,
                    i.Snippet.Thumbnails.High.Url));
    }

    public Task<string> ShortenUrl(Uri url)
        => ShortenUrl(url.ToString());

    public async Task<string> ShortenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentNullException(nameof(url));

        if (string.IsNullOrWhiteSpace(_creds.GetCreds().GoogleApiKey))
            return url;

        try
        {
            var response = await _sh.Url.Insert(new()
                                    {
                                        LongUrl = url
                                    })
                                    .ExecuteAsync();
            return response.Id;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            return url;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error shortening URL");
            return url;
        }
    }

    public async Task<IEnumerable<string>> GetPlaylistTracksAsync(string playlistId, int count = 50)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
            throw new ArgumentNullException(nameof(playlistId));

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        string nextPageToken = null;

        var toReturn = new List<string>(count);

        do
        {
            var toGet = count > 50 ? 50 : count;
            count -= toGet;

            var query = _yt.PlaylistItems.List("contentDetails");
            query.MaxResults = toGet;
            query.PlaylistId = playlistId;
            query.PageToken = nextPageToken;

            var data = await query.ExecuteAsync();

            toReturn.AddRange(data.Items.Select(i => i.ContentDetails.VideoId));
            nextPageToken = data.NextPageToken;
        } while (count > 0 && !string.IsNullOrWhiteSpace(nextPageToken));

        return toReturn;
    }

    public async Task<IReadOnlyDictionary<string, TimeSpan>> GetVideoDurationsAsync(IEnumerable<string> videoIds)
    {
        var videoIdsList = videoIds as List<string> ?? videoIds.ToList();

        var toReturn = new Dictionary<string, TimeSpan>();

        if (!videoIdsList.Any())
            return toReturn;
        var remaining = videoIdsList.Count;

        do
        {
            var toGet = remaining > 50 ? 50 : remaining;
            remaining -= toGet;

            var q = _yt.Videos.List("contentDetails");
            q.Id = string.Join(",", videoIdsList.Take(toGet));
            videoIdsList = videoIdsList.Skip(toGet).ToList();
            var items = (await q.ExecuteAsync()).Items;
            foreach (var i in items)
                toReturn.Add(i.Id, XmlConvert.ToTimeSpan(i.ContentDetails.Duration));
        } while (remaining > 0);

        return toReturn;
    }

    public async Task<string> Translate(string sourceText, string sourceLanguage, string targetLanguage)
    {
        string text;

        if (!Languages.ContainsKey(targetLanguage))
            throw new ArgumentException(nameof(sourceLanguage) + "/" + nameof(targetLanguage));

        if (string.IsNullOrWhiteSpace(sourceLanguage) || !Languages.ContainsKey(sourceLanguage))
            sourceLanguage = "auto";


        var url = new Uri(string.Format(
            "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}",
            ConvertToLanguageCode(sourceLanguage),
            ConvertToLanguageCode(targetLanguage),
            WebUtility.UrlEncode(sourceText)));
        using (var http = _httpFactory.CreateClient())
        {
            http.DefaultRequestHeaders.Add("user-agent",
                "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/41.0.2228.0 Safari/537.36");
            text = await http.GetStringAsync(url);
        }

        return string.Concat(JArray.Parse(text)[0].Select(x => x[0]));
    }

    private string ConvertToLanguageCode(string language)
    {
        Languages.TryGetValue(language, out var mode);
        return string.IsNullOrWhiteSpace(mode) ? language : mode;
    }
}

