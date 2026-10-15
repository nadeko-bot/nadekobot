using System.Buffers;
using System.Globalization;

namespace NadekoBot.Services;

public sealed record ToolManifest(string Name, string Version, string Digest, string Dir, long InstalledAt);

// yt-dlp, deno and ffmpeg which a manager installs in data/tools/<name>/<dir>/<name>. A tool without a valid
// "current" file is the program on PATH, so a bot which never got a tool runs as before.
public sealed class MediaTools
{
    public const string YT_DLP = "yt-dlp";
    public const string DENO = "deno";
    public const string FFMPEG = "ffmpeg";
    public const string CURRENT_FILE = "current";

    public const int MAX_VERSION_LENGTH = 64;

    public static readonly string[] Names = [YT_DLP, DENO, FFMPEG];

    // Absolute, because a relative executable path would depend on the working directory of each program.
    public static MediaTools Default { get; } = new(Path.GetFullPath(Path.Combine("data", "tools")));

    private static readonly SearchValues<char> _safeChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._+-");

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (DateTime Stamp, long Length, ToolManifest? Manifest)> _cache = new();

    public MediaTools(string root)
        => Root = root;

    public string Root { get; }

    public string YtDlp
        => ExecutablePath(YT_DLP) ?? YT_DLP;

    public string Ffmpeg
        => ExecutablePath(FFMPEG) ?? FFMPEG;

    // yt-dlp finds a deno on PATH by itself; only an installed one needs its path.
    public string? Deno
        => ExecutablePath(DENO);

    public static bool IsKnown(string name)
        => Array.IndexOf(Names, name) >= 0;

    // Versions and dir names become path segments, so only plain characters pass.
    public static bool IsSafeSegment(ReadOnlySpan<char> value)
        => value.Length is > 0 and <= MAX_VERSION_LENGTH
           && !value.ContainsAnyExcept(_safeChars)
           && value[0] != '.'
           && value[0] != '-';

    public string ToolDir(string name)
        => Path.Combine(Root, name);

    public string ExecutableIn(string name, string dir)
        => Path.Combine(Root, name, dir, name);

    public string? ExecutablePath(string name)
        => Manifest(name) is { } m ? ExecutableIn(name, m.Dir) : null;

    // The file is read again only when it changes, so a new tool is used from the next start of the program.
    public ToolManifest? Manifest(string name)
    {
        var path = Path.Combine(ToolDir(name), CURRENT_FILE);
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
                return Remember(name, default, -1, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(name, out var cached)
                && cached.Stamp == info.LastWriteTimeUtc
                && cached.Length == info.Length)
                return cached.Manifest;
        }

        ToolManifest? manifest = null;
        try
        {
            manifest = Parse(name, File.ReadAllText(path));
            if (manifest is not null && !File.Exists(ExecutableIn(name, manifest.Dir)))
                manifest = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (manifest is null)
            Log.Warning("Ignoring {Path}: it does not name an installed {Tool}", path, name);

        return Remember(name, info.LastWriteTimeUtc, info.Length, manifest);
    }

    private ToolManifest? Remember(string name, DateTime stamp, long length, ToolManifest? manifest)
    {
        lock (_lock)
            _cache[name] = (stamp, length, manifest);

        return manifest;
    }

    public static string Format(ToolManifest manifest)
        => $"version={manifest.Version}\ndigest={manifest.Digest}\ndir={manifest.Dir}\n"
           + $"installedAt={manifest.InstalledAt.ToString(CultureInfo.InvariantCulture)}\n";

    public static ToolManifest? Parse(string name, string text)
    {
        string? version = null, digest = null, dir = null;
        long installedAt = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var value = line[(eq + 1)..];
            switch (line[..eq])
            {
                case "version":
                    version = value;
                    break;
                case "digest":
                    digest = value;
                    break;
                case "dir":
                    dir = value;
                    break;
                case "installedAt":
                    long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out installedAt);
                    break;
            }
        }

        if (version is null || digest is null || dir is null || !IsSafeSegment(version) || !IsSafeSegment(dir)
            || !IsValidDigest(digest))
            return null;

        return new(name, version, digest, dir, installedAt);
    }

    // The upstream asset digest, as GitHub gives it: "sha256:" and 64 lowercase hex digits.
    public static bool IsValidDigest(ReadOnlySpan<char> value)
        => value.StartsWith("sha256:", StringComparison.Ordinal)
           && value.Length == 7 + 64
           && !value[7..].ContainsAnyExcept("0123456789abcdef");
}
