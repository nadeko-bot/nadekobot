using System.Net.Sockets;
using System.Text;

namespace NadekoBot.Modules.Music;

// The manager's music service for a managed bot, in data/.music-service. The bot owner can neither read nor change
// it: backups and restores leave the file out, no route returns the key, and searches.yml cannot move the url.
// The key is shared by many bots, so it must never reach a log line, an API answer or another host.
public sealed class MusicServiceSetting(string path)
{
    public const int MAX_ID_LENGTH = 64;
    public const int MAX_URL_LENGTH = 256;
    public const int MIN_KEY_LENGTH = 32;
    public const int MAX_KEY_LENGTH = 512;
    public const int MAX_FILE_BYTES = 4096;

    // A temp file this old is left from a write which stopped, not one which is running now.
    private static readonly TimeSpan _staleTempAge = TimeSpan.FromMinutes(1);

    private const string ID_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._:-";
    private const string KEY_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~+/=";

    public static MusicServiceSetting Default { get; } =
        new(Path.GetFullPath(Path.Combine("data", ".music-service")));

    // The bot and the API must share one instance, so a write updates the copy which the bot reads at once.
    public static MusicServiceSetting For(string path)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(full, Default.FilePath, comparison) ? Default : new(full);
    }

    private readonly Lock _lock = new();
    private (DateTime Stamp, long Length, Entry? Entry) _cache = (default, -2, null);
    private int _generation;

    public sealed record Entry(string Id, string BaseUrl, string Key, string Host, int Port);

    public string FilePath { get; } = path;

    public Entry? Current
    {
        get
        {
            DateTime stamp;
            long length;
            try
            {
                var info = new FileInfo(FilePath);
                (stamp, length) = info.Exists ? (info.LastWriteTimeUtc, info.Length) : (default, -1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            int seen;
            lock (_lock)
            {
                if (_cache.Stamp == stamp && _cache.Length == length)
                    return _cache.Entry;

                seen = _generation;
            }

            var entry = length < 0 ? null : ReadInternal(length);
            lock (_lock)
            {
                // A Set or Clear which ran during the read wins: its entry is newer than this one.
                if (_generation == seen)
                    _cache = (stamp, length, entry);
            }

            return entry;
        }
    }

    public static bool IsValidId(ReadOnlySpan<char> id)
        => id.Length is > 0 and <= MAX_ID_LENGTH && !id.ContainsAnyExcept(ID_CHARS);

    // RFC 7235 token68, so the key goes into an Authorization header unchanged.
    public static bool IsValidKey(ReadOnlySpan<char> key)
    {
        if (key.Length is < MIN_KEY_LENGTH or > MAX_KEY_LENGTH || key.ContainsAnyExcept(KEY_CHARS))
            return false;

        var padding = key.IndexOf('=');
        return padding < 0 || (padding > 0 && !key[padding..].ContainsAnyExcept('='));
    }

    public static bool TryCreate(string? id, string? url, string? key, out Entry entry)
    {
        entry = null!;
        if (id is null || !IsValidId(id) || key is null || !IsValidKey(key))
            return false;

        if (url is null
            || url.Length > MAX_URL_LENGTH
            || url.AsSpan().ContainsAnyInRange('\0', ' ')
            || url.Contains('\u007f')
            || MusicServiceClient.ParseBaseUrl(url) is not { } baseUrl
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.IdnHost))
            return false;

        entry = new(id, baseUrl, key, uri.IdnHost, uri.Port);
        return true;
    }

    // The caller checks the values with TryCreate first. The file holds the key, so it is created 0600.
    public void Set(Entry entry)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        DeleteStaleTempFiles();
        var temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        long length;
        DateTime stamp;
        try
        {
            using (var fs = new FileStream(temp, options))
            {
                fs.Write(Encoding.UTF8.GetBytes($"id={entry.Id}\nurl={entry.BaseUrl}\nkey={entry.Key}\n"));
                fs.Flush(flushToDisk: true);
                length = fs.Length;
            }

            // A rename keeps the time, so this describes the file which this call wrote, also when another write follows.
            stamp = File.GetLastWriteTimeUtc(temp);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }

        // The time and length of the file can stay the same for a new key, so the cache takes the entry directly.
        lock (_lock)
        {
            _generation++;
            _cache = (stamp, length, entry);
        }
    }

    public void Clear()
    {
        DeleteStaleTempFiles();
        if (File.Exists(FilePath))
            File.Delete(FilePath);

        lock (_lock)
        {
            _generation++;
            _cache = (default, -1, null);
        }
    }

    // A temp file of a write which stopped holds the key.
    public void DeleteStaleTempFiles()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;

        var oldest = DateTime.UtcNow - _staleTempAge;
        foreach (var file in Directory.EnumerateFiles(dir, Path.GetFileName(FilePath) + ".*.tmp"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < oldest)
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning("Could not delete the old temporary file {Path}: {Error}", file, ex.Message);
            }
        }
    }

    // The client of a managed bot connects only to the service which the manager set, also on a private address,
    // never to another host: a redirect or a changed url cannot send the key somewhere else.
    public SocketsHttpHandler CreateHandler()
        => new()
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectInternalAsync
        };

    private async ValueTask<Stream> ConnectInternalAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        if (Current is not { } entry
            || !string.Equals(ctx.DnsEndPoint.Host, entry.Host, StringComparison.OrdinalIgnoreCase)
            || ctx.DnsEndPoint.Port != entry.Port)
            throw new HttpRequestException("The request is not for the music service");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(ctx.DnsEndPoint, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private Entry? ReadInternal(long length)
    {
        if (length > MAX_FILE_BYTES)
        {
            Log.Warning("Ignoring {Path}: it is larger than {Max} bytes", FilePath, MAX_FILE_BYTES);
            return null;
        }

        string text;
        try
        {
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> buffer = stackalloc byte[MAX_FILE_BYTES + 1];
            var read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read > MAX_FILE_BYTES)
            {
                Log.Warning("Ignoring {Path}: it is larger than {Max} bytes", FilePath, MAX_FILE_BYTES);
                return null;
            }

            text = Encoding.UTF8.GetString(buffer[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string? id = null, url = null, key = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("id=", StringComparison.Ordinal))
                id = line[3..].TrimEnd('\r');
            else if (line.StartsWith("url=", StringComparison.Ordinal))
                url = line[4..].TrimEnd('\r');
            else if (line.StartsWith("key=", StringComparison.Ordinal))
                key = line[4..].TrimEnd('\r');
        }

        if (TryCreate(id, url, key, out var entry))
            return entry;

        // Never the content: the file can hold a key which is only half valid.
        Log.Warning("Ignoring {Path}: it is not a valid music service setting", FilePath);
        return null;
    }
}
