#nullable disable
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NadekoBot.Modules.Music.Services;

public sealed partial class YtLoader : INService
{
    private static readonly byte[] _ytResultInitialData = Encoding.UTF8.GetBytes("var ytInitialData = ");
    private static readonly byte[] _ytResultJsonEnd = Encoding.UTF8.GetBytes(";<");

    private static readonly string[] _durationFormats =
    [
        @"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss", @"hhh\:mm\:ss"
    ];

    private readonly IHttpClientFactory _httpFactory;

    public YtLoader(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    // public async Task<TrackInfo> LoadTrackByIdAsync(string videoId)
    // {
    //     using var http = new HttpClient();
    //     http.DefaultRequestHeaders.Add("X-YouTube-Client-Name", "1");
    //     http.DefaultRequestHeaders.Add("X-YouTube-Client-Version", "2.20210520.09.00");
    //     http.DefaultRequestHeaders.Add("Cookie", "CONSENT=YES+cb.20210530-19-p0.en+FX+071;");
    //
    //     var responseString = await http.GetStringAsync($"https://youtube.com?" +
    //                         $"pbj=1" +
    //                         $"&hl=en" +
    //                         $"&v=" + videoId);
    //
    //     var jsonDoc = JsonDocument.Parse(responseString).RootElement;
    //     var elem = jsonDoc.EnumerateArray()
    //         .FirstOrDefault(x => x.TryGetProperty("page", out var elem) && elem.GetString() == "watch");
    //
    //     var formatsJsonArray = elem.GetProperty("streamingdata")
    //         .GetProperty("formats")
    //         .GetRawText();
    //     
    //     var formats = JsonSerializer.Deserialize<List<YtAdaptiveFormat>>(formatsJsonArray);
    //     var result = formats
    //         .Where(x => x.MimeType.StartsWith("audio/"))
    //         .OrderByDescending(x => x.Bitrate)
    //         .FirstOrDefault();
    //
    //     if (result is null)
    //         return null;
    //
    //     return new YtTrackInfo("1", "2", TimeSpan.Zero);
    // }

    public async Task<IList<TrackInfo>> LoadResultsAsync(string query)
    {
        query = Uri.EscapeDataString(query);

        using var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Add("Cookie", "CONSENT=YES+cb.20210530-19-p0.en+FX+071;");

        byte[] response;
        try
        {
            response = await http.GetByteArrayAsync($"https://youtube.com/results?hl=en&search_query={query}");
        }
        catch (HttpRequestException ex)
        {
            Log.Warning("Unable to retrieve data with YtLoader: {ErrorMessage}", ex.Message);
            return null;
        }

        // there is a lot of useless html above the script tag, however if html gets significantly reduced
        // this will result in the json being cut off

        var mem = GetScriptResponseSpan(response);
        var root = JsonDocument.Parse(mem).RootElement;

        using var tracksJsonItems = root
                              .GetProperty("contents")
                              .GetProperty("twoColumnSearchResultsRenderer")
                              .GetProperty("primaryContents")
                              .GetProperty("sectionListRenderer")
                              .GetProperty("contents")[0]
                              .GetProperty("itemSectionRenderer")
                              .GetProperty("contents")
                              .EnumerateArray();

        var tracks = new List<TrackInfo>();
        foreach (var track in tracksJsonItems)
        {
            if (!track.TryGetProperty("videoRenderer", out var elem))
                continue;

            var videoId = elem.GetProperty("videoId").GetString();
            var thumb = elem.GetProperty("thumbnail").GetProperty("thumbnails")[0].GetProperty("url").GetString();
            var title = elem.GetProperty("title").GetProperty("runs")[0].GetProperty("text").GetString();
            var durationString = elem.GetProperty("lengthText").GetProperty("simpleText").GetString();

            if (!TimeSpan.TryParseExact(durationString,
                    _durationFormats,
                    CultureInfo.InvariantCulture,
                    out var duration))
            {
                Log.Warning("Cannot parse duration: {DurationString}", durationString);
                continue;
            }

            tracks.Add(new YtTrackInfo(title, videoId, thumb, duration));
            if (tracks.Count >= 5)
                break;
        }

        return tracks;
    }

    private Memory<byte> GetScriptResponseSpan(byte[] response)
    {
        var responseSpan = response.AsSpan()[140_000..];
        var startIndex = responseSpan.IndexOf(_ytResultInitialData);
        if (startIndex == -1)
            return null; // FUTURE try selecting html
        startIndex += _ytResultInitialData.Length;

        var endIndex =
            140_000 + startIndex + responseSpan[(startIndex + 20_000)..].IndexOf(_ytResultJsonEnd) + 20_000;
        startIndex += 140_000;
        return response.AsMemory(startIndex, endIndex - startIndex);
    }
}