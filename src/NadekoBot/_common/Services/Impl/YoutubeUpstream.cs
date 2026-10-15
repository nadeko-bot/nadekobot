using System.Net;
using System.Text;

namespace NadekoBot.Services;

// An http:// proxy, optionally with a user and a password, which PublicOnlyProxy connects through.
public sealed record UpstreamProxy(DnsEndPoint EndPoint, string? Authorization, string? Password)
{
    public const int MAX_URL_LENGTH = 512;

    public static bool TryParse(string? value, out UpstreamProxy proxy)
    {
        proxy = null!;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MAX_URL_LENGTH
            || value.AsSpan().ContainsAnyInRange('\0', ' ')
            || value.Contains('\u007f')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || string.IsNullOrEmpty(uri.IdnHost)
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
            return false;

        string? authorization = null;
        string? password = null;
        if (uri.UserInfo.Length > 0)
        {
            var colon = uri.UserInfo.IndexOf(':');
            var user = Uri.UnescapeDataString(colon < 0 ? uri.UserInfo : uri.UserInfo[..colon]);
            password = colon < 0 ? string.Empty : Uri.UnescapeDataString(uri.UserInfo[(colon + 1)..]);
            authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        }

        proxy = new(new DnsEndPoint(uri.IdnHost, uri.Port), authorization, password);
        return true;
    }
}

// The hosts which YouTube audio needs. Only these go through the YouTube proxy of a managed bot.
public static class YoutubeHosts
{
    private static readonly string[] _domains =
        ["youtube.com", "youtu.be", "googlevideo.com", "youtube-nocookie.com", "youtubei.googleapis.com"];

    public static bool Matches(string host, int port)
    {
        if (port is not (80 or 443) || IPAddress.TryParse(host, out _))
            return false;

        var name = host.TrimEnd('.');
        foreach (var domain in _domains)
        {
            if (name.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || (name.Length > domain.Length
                    && name[^(domain.Length + 1)] == '.'
                    && name.EndsWith(domain, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}

// The manager's YouTube proxy for this bot, in data/.youtube-proxy. The file is the only copy, so the API and the
// bot always agree, and the bot reads it again only when it changes.
public sealed class YoutubeUpstreamSetting(string path)
{
    public const int MAX_ID_LENGTH = 64;

    public static YoutubeUpstreamSetting Default { get; } = new(Path.GetFullPath(Path.Combine("data", ".youtube-proxy")));

    private readonly Lock _lock = new();
    private (DateTime Stamp, long Length, Entry? Entry) _cache = (default, -2, null);

    public sealed record Entry(string Id, UpstreamProxy Proxy);

    public string FilePath { get; } = path;

    public UpstreamProxy? Proxy
        => Current?.Proxy;

    public string? Id
        => Current?.Id;

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

            lock (_lock)
            {
                if (_cache.Stamp == stamp && _cache.Length == length)
                    return _cache.Entry;
            }

            var entry = length < 0 ? null : ReadInternal();
            lock (_lock)
                _cache = (stamp, length, entry);

            return entry;
        }
    }

    public static bool IsValidId(ReadOnlySpan<char> id)
        => id.Length is > 0 and <= MAX_ID_LENGTH
           && !id.ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._:-");

    // The caller checks the id and the url first. The file holds a password, so it is created 0600.
    public void Set(string id, string url)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var fs = new FileStream(temp, options))
            {
                fs.Write(Encoding.UTF8.GetBytes($"id={id}\nurl={url}\n"));
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    private Entry? ReadInternal()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string? id = null, url = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("id=", StringComparison.Ordinal))
                id = line[3..].TrimEnd('\r');
            else if (line.StartsWith("url=", StringComparison.Ordinal))
                url = line[4..].TrimEnd('\r');
        }

        if (id is not null && IsValidId(id) && UpstreamProxy.TryParse(url, out var proxy))
            return new(id, proxy);

        Log.Warning("Ignoring {Path}: it is not a valid YouTube proxy", FilePath);
        return null;
    }
}
