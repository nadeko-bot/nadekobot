using System.Globalization;
using System.Text.RegularExpressions;
using NadekoBot.Modules.Searches;

namespace NadekoBot.Modules.Music;

public sealed class YtdlYoutubeResolver : IYoutubeResolver
{
    private static readonly string[] _durationFormats =
    [
        "ss", "m\\:ss", "mm\\:ss", "h\\:mm\\:ss", "hh\\:mm\\:ss", "hhh\\:mm\\:ss"
    ];

    private static readonly Regex _expiryRegex = new(@"(?:[\?\&]expire\=(?<timestamp>\d+))");


    private static readonly Regex _simplePlaylistRegex = new(@"&list=(?<id>[\w\-]{12,})", RegexOptions.Compiled);


    private readonly ITrackCacher _trackCacher;

    private readonly YtdlOperation _ytdlPlaylistOperation;
    private readonly YtdlOperation _ytdlIdOperation;
    private readonly YtdlOperation _ytdlSearchOperation;

    private readonly IGoogleApiService _google;

    public YtdlYoutubeResolver(ITrackCacher trackCacher, IGoogleApiService google, SearchesConfigService scs)
    {
        _trackCacher = trackCacher;
        _google = google;


        _ytdlPlaylistOperation = new("-4 "
                                     + "--geo-bypass "
                                     + "--encoding UTF8 "
                                     + "-f bestaudio "
                                     + "-e "
                                     + "--get-url "
                                     + "--get-id "
                                     + "--get-thumbnail "
                                     + "--get-duration "
                                     + "--no-check-certificate "
                                     + "-i "
                                     + "--yes-playlist "
                                     + "-- \"{0}\"");

        _ytdlIdOperation = new("-4 "
                               + "--geo-bypass "
                               + "--encoding UTF8 "
                               + "-f bestaudio "
                               + "-e "
                               + "--get-url "
                               + "--get-id "
                               + "--get-thumbnail "
                               + "--get-duration "
                               + "--no-check-certificate "
                               + "-- \"{0}\"");

        _ytdlSearchOperation = new("-4 "
                                   + "--geo-bypass "
                                   + "--encoding UTF8 "
                                   + "-f bestaudio "
                                   + "-e "
                                   + "--get-url "
                                   + "--get-id "
                                   + "--get-thumbnail "
                                   + "--get-duration "
                                   + "--no-check-certificate "
                                   + "--default-search "
                                   + "\"ytsearch:\" -- \"{0}\"");
    }

    private YtTrackData ResolveYtdlData(string ytdlOutputString)
    {
        if (string.IsNullOrWhiteSpace(ytdlOutputString))
            return default;

        var dataArray = ytdlOutputString.Trim().Split('\n');

        if (dataArray.Length < 5)
        {
            Log.Information("Not enough data received: {YtdlData}", ytdlOutputString);
            return default;
        }

        if (!TimeSpan.TryParseExact(dataArray[4], _durationFormats, CultureInfo.InvariantCulture, out var time))
            time = TimeSpan.Zero;

        var thumbnail = Uri.IsWellFormedUriString(dataArray[3], UriKind.Absolute) ? dataArray[3].Trim() : string.Empty;

        return new(dataArray[0], dataArray[1], thumbnail, dataArray[2], time);
    }

    private ITrackInfo DataToInfo(in YtTrackData trackData)
        => new RemoteTrackInfo(
            trackData.Id,
            trackData.Title,
            $"https://youtube.com/watch?v={trackData.Id}",
            trackData.Thumbnail,
            trackData.Duration,
            MusicPlatform.Youtube);

    private Func<Task<string?>> CreateCacherFactory(string id)
        => () => _trackCacher.GetOrCreateStreamLink(id,
            MusicPlatform.Youtube,
            async () => await ExtractNewStreamUrlAsync(id));

    private static TimeSpan GetExpiry(string streamUrl)
    {
        var match = _expiryRegex.Match(streamUrl);
        if (match.Success && double.TryParse(match.Groups["timestamp"].ToString(), out var timestamp))
        {
            var realExpiry = timestamp.ToUnixTimestamp() - DateTime.UtcNow;
            if (realExpiry > TimeSpan.FromMinutes(60))
                return realExpiry.Subtract(TimeSpan.FromMinutes(30));

            return realExpiry;
        }

        return TimeSpan.FromHours(1);
    }

    private async Task<(string StreamUrl, TimeSpan Expiry)> ExtractNewStreamUrlAsync(string id)
    {
        var data = await _ytdlIdOperation.GetDataAsync(id);
        var trackInfo = ResolveYtdlData(data);
        if (string.IsNullOrWhiteSpace(trackInfo.StreamUrl))
            return default;

        return (trackInfo.StreamUrl!, GetExpiry(trackInfo.StreamUrl!));
    }

    public async Task<ITrackInfo?> ResolveByIdAsync(string id)
    {
        id = id.Trim();

        var cachedData = await _trackCacher.GetCachedDataByIdAsync(id, MusicPlatform.Youtube);
        if (cachedData is null)
        {
            Log.Information("Resolving youtube track by Id: {YoutubeId}", id);

            var data = await _ytdlIdOperation.GetDataAsync(id);

            var trackInfo = ResolveYtdlData(data);
            if (string.IsNullOrWhiteSpace(trackInfo.Title))
                return default;

            var toReturn = DataToInfo(in trackInfo);

            await Task.WhenAll(_trackCacher.CacheTrackDataAsync(toReturn.ToCachedData(id)),
                CacheStreamUrlAsync(trackInfo));

            return toReturn;
        }

        return DataToInfo(new(cachedData.Title, cachedData.Id, cachedData.Thumbnail, null, cachedData.Duration));
    }

    private Task CacheStreamUrlAsync(YtTrackData trackInfo)
        => _trackCacher.CacheStreamUrlAsync(trackInfo.Id,
            MusicPlatform.Youtube,
            trackInfo.StreamUrl!,
            GetExpiry(trackInfo.StreamUrl!));

    public async IAsyncEnumerable<ITrackInfo> ResolveTracksByPlaylistIdAsync(string playlistId)
    {
        Log.Information("Resolving youtube tracks from playlist: {PlaylistId}", playlistId);
        var count = 0;

        var ids = await _trackCacher.GetPlaylistTrackIdsAsync(playlistId, MusicPlatform.Youtube);
        if (ids.Count > 0)
        {
            foreach (var id in ids)
            {
                var trackInfo = await ResolveByIdAsync(id);
                if (trackInfo is null)
                    continue;

                yield return trackInfo;
            }

            yield break;
        }

        var data = string.Empty;
        var trackIds = new List<string>();
        await foreach (var line in _ytdlPlaylistOperation.EnumerateDataAsync(playlistId))
        {
            data += line;

            if (++count == 5)
            {
                var trackData = ResolveYtdlData(data);
                data = string.Empty;
                count = 0;
                if (string.IsNullOrWhiteSpace(trackData.Id))
                    continue;

                var info = DataToInfo(in trackData);
                await Task.WhenAll(_trackCacher.CacheTrackDataAsync(info.ToCachedData(trackData.Id)),
                    CacheStreamUrlAsync(trackData));

                trackIds.Add(trackData.Id);
                yield return info;
            }
            else
                data += Environment.NewLine;
        }

        await _trackCacher.CachePlaylistTrackIdsAsync(playlistId, MusicPlatform.Youtube, trackIds);
    }

    public async IAsyncEnumerable<ITrackInfo> ResolveTracksFromPlaylistAsync(string query)
    {
        string? playlistId;
        // try to match playlist id inside the query, if a playlist url has been queried
        var match = _simplePlaylistRegex.Match(query);
        if (match.Success)
        {
            // if it's a success, just return from that playlist using the id
            playlistId = match.Groups["id"].ToString();
            await foreach (var track in ResolveTracksByPlaylistIdAsync(playlistId))
                yield return track;

            yield break;
        }

        // if a query is a search term, try the cache
        playlistId = await _trackCacher.GetPlaylistIdByQueryAsync(query, MusicPlatform.Youtube);
        if (playlistId is null)
        {
            // if it's not in the cache
            // find playlist id by keyword using google api
            try
            {
                var playlistIds = await _google.GetPlaylistIdsByKeywordsAsync(query);
                playlistId = playlistIds.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error Getting playlist id via GoogleApi");
            }

            // if query is not a playlist url
            // and query result is not in the cache
            // and api returns no values
            // it means invalid input has been used,
            // or google api key is not provided
            if (playlistId is null)
                yield break;
        }

        // cache the query -> playlist id for fast future lookup
        await _trackCacher.CachePlaylistIdByQueryAsync(query, MusicPlatform.Youtube, playlistId);
        await foreach (var track in ResolveTracksByPlaylistIdAsync(playlistId))
            yield return track;
    }

    public Task<ITrackInfo?> ResolveByQueryAsync(string query)
        => ResolveByQueryAsync(query, true);

    public async Task<ITrackInfo?> ResolveByQueryAsync(string query, bool tryResolving)
    {
        if (tryResolving)
        {
            var match = YoutubeHelpers.YtVideoIdRegex.Match(query);
            if (match.Success)
                return await ResolveByIdAsync(match.Groups["id"].Value);
        }

        Log.Information("Resolving youtube song by search term: {YoutubeQuery}", query);

        var cachedData = await _trackCacher.GetCachedDataByQueryAsync(query, MusicPlatform.Youtube);
        if (cachedData is null || string.IsNullOrWhiteSpace(cachedData.Title))
        {
            var stringData = await _ytdlSearchOperation.GetDataAsync(query);
            var trackData = ResolveYtdlData(stringData);

            var trackInfo = DataToInfo(trackData);
            await Task.WhenAll(_trackCacher.CacheTrackDataByQueryAsync(query, trackInfo.ToCachedData(trackData.Id)),
                CacheStreamUrlAsync(trackData));
            return trackInfo;
        }

        return DataToInfo(new(cachedData.Title, cachedData.Id, cachedData.Thumbnail, null, cachedData.Duration));
    }

    public Task<string?> GetStreamUrl(string videoId)
        => CreateCacherFactory(videoId)();

    private readonly struct YtTrackData
    {
        public readonly string Title;
        public readonly string Id;
        public readonly string Thumbnail;
        public readonly string? StreamUrl;
        public readonly TimeSpan Duration;

        public YtTrackData(
            string title,
            string id,
            string thumbnail,
            string? streamUrl,
            TimeSpan duration)
        {
            Title = title.Trim();
            Id = id.Trim();
            Thumbnail = thumbnail;
            StreamUrl = streamUrl;
            Duration = duration;
        }
    }
}