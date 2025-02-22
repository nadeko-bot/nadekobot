using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NadekoBot.Modules.Music.Resolvers;

public sealed class LocalTrackResolver : ILocalTrackResolver
{
    private static readonly HashSet<string> _musicExtensions = new[]
    {
        ".MP4", ".MP3", ".FLAC", ".OGG", ".WAV", ".WMA", ".WMV", ".AAC", ".MKV", ".WEBM", ".M4A", ".AA", ".AAX",
        ".ALAC", ".AIFF", ".MOV", ".FLV", ".OGG", ".M4V"
    }.ToHashSet();

    public async Task<ITrackInfo?> ResolveByQueryAsync(string query)
    {
        if (!File.Exists(query))
            return null;

        var trackDuration = await Ffprobe.GetTrackDurationAsync(query);
        return new SimpleTrackInfo(Path.GetFileNameWithoutExtension(query),
            $"https://google.com?q={Uri.EscapeDataString(Path.GetFileNameWithoutExtension(query))}",
            "https://cdn.discordapp.com/attachments/155726317222887425/261850914783100928/1482522077_music.png",
            trackDuration,
            MusicPlatform.Local,
            $"\"{Path.GetFullPath(query)}\"");
    }

    public async IAsyncEnumerable<ITrackInfo> ResolveDirectoryAsync(string dirPath)
    {
        DirectoryInfo dir;
        try
        {
            dir = new(dirPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Specified directory {DirectoryPath} could not be opened", dirPath);
            yield break;
        }

        var files = dir.EnumerateFiles()
                       .Where(x =>
                       {
                           if (!x.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System)
                               && _musicExtensions.Contains(x.Extension.ToUpperInvariant()))
                               return true;
                           return false;
                       })
                       .ToList();

        var firstFile = files.FirstOrDefault()?.FullName;
        if (firstFile is null)
            yield break;

        var firstData = await ResolveByQueryAsync(firstFile);
        if (firstData is not null)
            yield return firstData;

        var fileChunks = files.Skip(1).Chunk(10);
        foreach (var chunk in fileChunks)
        {
            var part = await chunk.Select(x => ResolveByQueryAsync(x.FullName)).WhenAll();

            // nullable reference types being annoying
            foreach (var p in part)
            {
                if (p is null)
                    continue;

                yield return p;
            }
        }
    }
}

public static class Ffprobe
{
    public static async Task<TimeSpan> GetTrackDurationAsync(string query)
    {
        query = query.Replace("\"", "");

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments =
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 -- \"{query}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            });

            if (p is null)
                return TimeSpan.Zero;

            var data = await p.StandardOutput.ReadToEndAsync();
            if (double.TryParse(data, out var seconds))
                return TimeSpan.FromSeconds(seconds);

            var errorData = await p.StandardError.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(errorData))
                Log.Warning("Ffprobe warning for file {FileName}: {ErrorMessage}", query, errorData);

            return TimeSpan.Zero;
        }
        catch (Win32Exception)
        {
            Log.Warning("Ffprobe was likely not installed. Local song durations will show as (?)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unknown exception running ffprobe; {ErrorMessage}", ex.Message);
        }

        return TimeSpan.Zero;
    }
}