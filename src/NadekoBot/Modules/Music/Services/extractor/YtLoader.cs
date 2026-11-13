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
    private readonly BotPolicy _policy;
    private readonly PublicOnlyProxy _proxy;

    public YtLoader(IHttpClientFactory httpFactory, BotPolicy policy, PublicOnlyProxy proxy)
    {
        _httpFactory = httpFactory;
        _policy = policy;
        _proxy = proxy;
    }

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

        // A managed bot with a YouTube proxy sends every YouTube request through it.
        using var http = _policy.Managed && _proxy.HasYoutubeUpstream
            ? _httpFactory.CreateClient(AudioFileCacheService.YOUTUBE_HTTP_CLIENT_NAME)
            : _httpFactory.CreateClient();
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
        try
        {
            using var doc = JsonDocument.Parse(mem);

            using var tracksJsonItems = doc.RootElement
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

                // A live stream has no lengthText, so the bot skips it.
                var videoId = StringInternal(PropertyInternal(elem, "videoId"));
                var durationString = StringInternal(PropertyInternal(PropertyInternal(elem, "lengthText"), "simpleText"));
                if (videoId is null || durationString is null)
                    continue;

                var title = StringInternal(PropertyInternal(
                                FirstInternal(PropertyInternal(PropertyInternal(elem, "title"), "runs")), "text"))
                            ?? videoId;
                var thumb = StringInternal(PropertyInternal(
                                FirstInternal(PropertyInternal(PropertyInternal(elem, "thumbnail"), "thumbnails")), "url"))
                            ?? string.Empty;

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
        catch (Exception ex) when (ex is JsonException
                                       or KeyNotFoundException
                                       or InvalidOperationException
                                       or IndexOutOfRangeException)
        {
            SiteAnswer.LogUnreadable("YouTube", ex);
            return null;
        }
    }

    private static JsonElement PropertyInternal(JsonElement elem, string name)
        => elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(name, out var value) ? value : default;

    private static JsonElement FirstInternal(JsonElement elem)
        => elem.ValueKind == JsonValueKind.Array && elem.GetArrayLength() > 0 ? elem[0] : default;

    private static string StringInternal(JsonElement elem)
        => elem.ValueKind == JsonValueKind.String ? elem.GetString() : null;

    // An empty result makes the parse fail, so a short page (consent or block page) gets the unreadable answer warning.
    private Memory<byte> GetScriptResponseSpan(byte[] response)
    {
        if (response.Length <= 140_000)
            return default;

        var responseSpan = response.AsSpan()[140_000..];
        var startIndex = responseSpan.IndexOf(_ytResultInitialData);
        if (startIndex == -1)
            return default; // FUTURE try selecting html
        startIndex += _ytResultInitialData.Length;

        if (startIndex + 20_000 > responseSpan.Length)
            return default;

        var end = responseSpan[(startIndex + 20_000)..].IndexOf(_ytResultJsonEnd);
        if (end == -1)
            return default;

        var endIndex = 140_000 + startIndex + end + 20_000;
        startIndex += 140_000;
        return response.AsMemory(startIndex, endIndex - startIndex);
    }
}