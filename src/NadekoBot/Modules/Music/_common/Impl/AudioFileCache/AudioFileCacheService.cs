using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Modules.Music;

using InFlightDict = System.Collections.Concurrent.ConcurrentDictionary<string, (Task DownloadTask, CacheFileState State)>;

public sealed class AudioFileCacheService : INService, IReadyExecutor
{
    private const int MAX_DOWNLOAD_RETRIES = 3;
    private const int EVICTION_CHECK_MINUTES = 30;
    private const int DOWNLOAD_BUFFER_SIZE = 81_920;
    private const string CACHE_DIRECTORY = "data/music_cache";
    private const int MANAGED_MAX_CACHE_GB = 1;
    private const int STALE_PARTIAL_FILE_HOURS = 24;
    private const long MAX_CACHE_TRACK_BYTES = 200L * 1024 * 1024;

    public const string HTTP_CLIENT_NAME = "music-cache";

    private readonly AudioFileCacheConfigService _configService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly BotPolicy _policy;
    private readonly InFlightDict _inFlightDownloads = new();

    public AudioFileCacheService(
        AudioFileCacheConfigService configService,
        IHttpClientFactory httpFactory,
        BotPolicy policy)
    {
        _policy = policy;
        _configService = configService;
        _httpFactory = httpFactory;
    }

    public Task OnReadyAsync()
    {
        EnsureCacheDirectory();
        CleanupStalePartialFiles();

        _ = Task.Run(EvictionLoopAsync);
        return Task.CompletedTask;
    }

    public (string? FilePath, CacheFileState? State) GetOrStartDownload(
        string trackId,
        MusicPlatform platform,
        Func<Task<string?>> streamUrlFactory)
    {
        if (platform == MusicPlatform.Radio)
            return (null, null);

        var cacheKey = GetCacheKey(trackId, platform);
        var filePath = GetFilePath(cacheKey);

        // fully cached - return immediately
        if (File.Exists(filePath))
        {
            try { File.SetLastAccessTimeUtc(filePath, DateTime.UtcNow); }
            catch { /* non-critical */ }

            return (filePath, null);
        }

        var partialPath = filePath + ".partial";

        // completed download that wasn't renamed yet (reader had file open last time)
        if (File.Exists(partialPath) && !_inFlightDownloads.ContainsKey(cacheKey))
        {
            try
            {
                File.Move(partialPath, filePath, overwrite: true);
                return (filePath, null);
            }
            catch (IOException)
            {
                // still locked, serve from partial
                return (partialPath, null);
            }
        }

        // check if there's already an in-flight download for this key
        if (_inFlightDownloads.TryGetValue(cacheKey, out var existing))
            return (partialPath, existing.State);

        // start a new download
        var state = new CacheFileState();
        var downloadTask = Task.Run(() => DownloadInternalAsync(filePath, partialPath, streamUrlFactory, state));

        if (_inFlightDownloads.TryAdd(cacheKey, (downloadTask, state)))
        {
            // clean up the dictionary entry when the download finishes
            _ = downloadTask.ContinueWith(_ =>
                {
                    if (_inFlightDownloads.TryRemove(cacheKey, out var removed))
                        removed.State.Dispose();
                },
                TaskContinuationOptions.ExecuteSynchronously);

            return (partialPath, state);
        }

        // race: another thread added first, use theirs
        if (_inFlightDownloads.TryGetValue(cacheKey, out existing))
            return (partialPath, existing.State);

        return (null, null);
    }

    public string? GetCachedPath(string trackId, MusicPlatform platform)
    {
        var cacheKey = GetCacheKey(trackId, platform);
        var filePath = GetFilePath(cacheKey);
        return File.Exists(filePath) ? filePath : null;
    }

    private async Task DownloadInternalAsync(
        string finalPath,
        string partialPath,
        Func<Task<string?>> streamUrlFactory,
        CacheFileState state)
    {
        var dir = Path.GetDirectoryName(finalPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var tooLarge = false;
        for (var attempt = 0; attempt < MAX_DOWNLOAD_RETRIES; attempt++)
        {
            try
            {
                var streamUrl = await streamUrlFactory();
                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    Log.Warning("Stream URL factory returned null/empty for {Path}", finalPath);
                    state.MarkFailed();
                    return;
                }

                using var http = _httpFactory.CreateClient(HTTP_CLIENT_NAME);
                http.Timeout = TimeSpan.FromMinutes(10);

                long existingBytes = 0;
                if (File.Exists(partialPath))
                    existingBytes = new FileInfo(partialPath).Length;

                state.UpdateBytesWritten(existingBytes);

                using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                if (existingBytes > 0)
                    request.Headers.Range = new RangeHeaderValue(existingBytes, null);

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    break;

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    Log.Debug("Stream URL expired (403), will re-resolve on retry {Attempt}/{Max}",
                        attempt + 1, MAX_DOWNLOAD_RETRIES);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var mode = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent
                    ? FileMode.Append
                    : FileMode.Create;

                // A live or endless stream has no length, and it would fill the disk.
                var startBytes = mode == FileMode.Append ? existingBytes : 0;
                if (response.Content.Headers.ContentLength is not { } length
                    || startBytes + length > MAX_CACHE_TRACK_BYTES)
                {
                    tooLarge = true;
                    break;
                }

                await using (var fileStream = new FileStream(
                                 partialPath, mode, FileAccess.Write, FileShare.ReadWrite,
                                 bufferSize: DOWNLOAD_BUFFER_SIZE, useAsync: true))
                {
                    await using var contentStream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[DOWNLOAD_BUFFER_SIZE];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        if (fileStream.Position + bytesRead > MAX_CACHE_TRACK_BYTES)
                        {
                            tooLarge = true;
                            break;
                        }

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        await fileStream.FlushAsync();
                        state.UpdateBytesWritten(fileStream.Position);
                    }
                }

                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                Log.Warning(ex, "Download attempt {Attempt}/{Max} failed for {Path}",
                    attempt + 1, MAX_DOWNLOAD_RETRIES, finalPath);

                if (attempt + 1 < MAX_DOWNLOAD_RETRIES)
                    await Task.Delay(1000 * (attempt + 1));
            }
        }

        if (tooLarge)
        {
            Log.Information("Not caching {Path}: the stream has no length or is too large", finalPath);
        }
        else if (File.Exists(partialPath) && new FileInfo(partialPath).Length > 0)
        {
            // mark complete first so readers know the data is all there
            state.MarkComplete();

            // try to rename to final path; if it fails (e.g. reader still has file open on Windows)
            // the file will be renamed on next access via TryFinalizePartialFile
            try
            {
                File.Move(partialPath, finalPath, overwrite: true);
            }
            catch (IOException)
            {
                // reader still has the file open - will be finalized later
            }

            return;
        }

        state.MarkFailed();
        TryDeleteFile(partialPath);
    }

    private async Task EvictionLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(EVICTION_CHECK_MINUTES));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                EvictIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during music cache eviction");
            }
        }
    }

    private void EvictIfNeeded()
    {
        if (!Directory.Exists(CACHE_DIRECTORY))
            return;

        var files = new DirectoryInfo(CACHE_DIRECTORY)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.EndsWith(".partial", StringComparison.Ordinal))
            .OrderBy(f => f.LastAccessTimeUtc)
            .ToList();

        var totalBytes = files.Sum(f => f.Length);
        var maxGb = _policy.Managed
            ? Math.Min(_configService.Data.MaxCacheSizeGb, MANAGED_MAX_CACHE_GB)
            : _configService.Data.MaxCacheSizeGb;
        var maxBytes = maxGb * 1024L * 1024L * 1024L;
        var targetBytes = (long)(maxBytes * 0.9);

        if (totalBytes <= maxBytes)
            return;

        Log.Debug("Music cache size {CurrentMb} MB exceeds limit {MaxGb} GB, evicting oldest files",
            totalBytes / (1024 * 1024), _configService.Data.MaxCacheSizeGb);

        foreach (var file in files)
        {
            if (totalBytes <= targetBytes)
                break;

            try
            {
                totalBytes -= file.Length;
                file.Delete();
                Log.Debug("Evicted cached audio file: {FileName}", file.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to evict cache file {FileName}", file.Name);
            }
        }
    }

    private static void EnsureCacheDirectory()
    {
        var ytDir = Path.Combine(CACHE_DIRECTORY, "yt");
        Directory.CreateDirectory(ytDir);
    }

    private static void CleanupStalePartialFiles()
    {
        if (!Directory.Exists(CACHE_DIRECTORY))
            return;

        var cutoff = DateTime.UtcNow.AddHours(-STALE_PARTIAL_FILE_HOURS);
        var partials = new DirectoryInfo(CACHE_DIRECTORY)
            .EnumerateFiles("*.partial", SearchOption.AllDirectories);

        foreach (var file in partials)
        {
            if (file.LastWriteTimeUtc < cutoff)
            {
                TryDeleteFile(file.FullName);
                Log.Debug("Cleaned up stale partial file: {FileName}", file.Name);
            }
        }
    }

    private const int MAX_YOUTUBE_ID_LENGTH = 64;

    private static readonly SearchValues<char> _youtubeIdChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    private static readonly string _cacheRoot = Path.GetFullPath(CACHE_DIRECTORY) + Path.DirectorySeparatorChar;

    // A track id from yt-dlp's generic extractor is the last part of any URL, so only a real YouTube id is a file name.
    public static string GetCacheKey(string trackId, MusicPlatform platform)
    {
        if (platform == MusicPlatform.Youtube
            && trackId.Length is > 0 and <= MAX_YOUTUBE_ID_LENGTH
            && !trackId.AsSpan().ContainsAnyExcept(_youtubeIdChars))
            return $"yt/{trackId}";

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(trackId), hash);
        var prefix = platform == MusicPlatform.Youtube ? "ytx" : "other";
        return $"{prefix}/{Convert.ToHexStringLower(hash)}";
    }

    private static string GetFilePath(string cacheKey)
    {
        var path = Path.GetFullPath(Path.Combine(CACHE_DIRECTORY, cacheKey + ".webm"));
        if (!path.StartsWith(_cacheRoot, StringComparison.Ordinal))
            throw new InvalidOperationException("Music cache path is outside the cache directory");

        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* best effort */ }
    }
}
