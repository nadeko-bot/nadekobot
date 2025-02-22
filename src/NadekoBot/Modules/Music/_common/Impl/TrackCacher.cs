namespace NadekoBot.Modules.Music;

public sealed class TrackCacher : ITrackCacher
{
    private readonly IBotCache _cache;

    public TrackCacher(IBotCache cache)
        => _cache = cache;


    private TypedKey<string> GetStreamLinkKey(MusicPlatform platform, string id)
        => new($"music:stream:{platform}:{id}");
    
    public async Task<string?> GetOrCreateStreamLink(
        string id,
        MusicPlatform platform,
        Func<Task<(string StreamUrl, TimeSpan Expiry)>> streamUrlFactory)
    {
        var key = GetStreamLinkKey(platform, id);

        var streamUrl = await _cache.GetOrDefaultAsync(key);
        await _cache.RemoveAsync(key);
        
        if (streamUrl == default)
        {
            (streamUrl, _) = await streamUrlFactory();
        }

        // make a new one for later use
        _ = Task.Run(async () =>
        {
            (streamUrl, var expiry) = await streamUrlFactory();
            await CacheStreamUrlAsync(id, platform, streamUrl, expiry);
        });
        
        return streamUrl;
    }

    public async Task CacheStreamUrlAsync(
        string id,
        MusicPlatform platform,
        string url,
        TimeSpan expiry)
        => await _cache.AddAsync(GetStreamLinkKey(platform, id), url, expiry);

    // track data by id
    private TypedKey<CachableTrackData> GetTrackDataKey(MusicPlatform platform, string id)
        => new($"music:track:{platform}:{id}");
    public async Task CacheTrackDataAsync(ICachableTrackData data)
        => await _cache.AddAsync(GetTrackDataKey(data.Platform, data.Id), ToCachableTrackData(data));

    private CachableTrackData ToCachableTrackData(ICachableTrackData data)
        => new CachableTrackData()
        {
            Id = data.Id,
            Platform = data.Platform,
            Thumbnail = data.Thumbnail,
            Title = data.Title,
            Url = data.Url,
        };

    public async Task<ICachableTrackData?> GetCachedDataByIdAsync(string id, MusicPlatform platform)
        => await _cache.GetOrDefaultAsync(GetTrackDataKey(platform, id)); 
    
    
    // track data by query
    private TypedKey<CachableTrackData> GetTrackDataQueryKey(MusicPlatform platform, string query)
        => new($"music:track:{platform}:q:{query}");

    public async Task CacheTrackDataByQueryAsync(string query, ICachableTrackData data)
        => await Task.WhenAll(
            _cache.AddAsync(GetTrackDataQueryKey(data.Platform, query), ToCachableTrackData(data)).AsTask(),
            _cache.AddAsync(GetTrackDataKey(data.Platform, data.Id), ToCachableTrackData(data)).AsTask());
    
    public async Task<ICachableTrackData?> GetCachedDataByQueryAsync(string query, MusicPlatform platform)
        => await _cache.GetOrDefaultAsync(GetTrackDataQueryKey(platform, query));


    // playlist track ids by playlist id
    private TypedKey<List<string>> GetPlaylistTracksCacheKey(string playlist, MusicPlatform platform)
        => new($"music:playlist_tracks:{platform}:{playlist}");

    public async Task CachePlaylistTrackIdsAsync(string playlistId, MusicPlatform platform, IEnumerable<string> ids)
        => await _cache.AddAsync(GetPlaylistTracksCacheKey(playlistId, platform), ids.ToList());

    public async Task<IReadOnlyCollection<string>> GetPlaylistTrackIdsAsync(string playlistId, MusicPlatform platform)
    {
        var result = await _cache.GetAsync(GetPlaylistTracksCacheKey(playlistId, platform));
        if (result.TryGetValue(out var val))
            return val;

        return Array.Empty<string>();
    }


    // playlist id by query
    private TypedKey<string> GetPlaylistCacheKey(string query, MusicPlatform platform)
        => new($"music:playlist_id:{platform}:{query}");
    
    public async Task CachePlaylistIdByQueryAsync(string query, MusicPlatform platform, string playlistId)
        => await _cache.AddAsync(GetPlaylistCacheKey(query, platform), playlistId);

    public async Task<string?> GetPlaylistIdByQueryAsync(string query, MusicPlatform platform)
        => await _cache.GetOrDefaultAsync(GetPlaylistCacheKey(query, platform));
}