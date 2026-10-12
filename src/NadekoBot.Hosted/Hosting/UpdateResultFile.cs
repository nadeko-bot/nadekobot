using System.Text;
using System.Text.RegularExpressions;

namespace NadekoBot.Hosted;

// Root writes this file before the start, but the bot can write data/ too, so the bot reads it as untrusted.
public static partial class UpdateResultFile
{
    public const int MAX_BYTES = 256;

    [GeneratedRegex(@"\A(?:(?<result>applied|rolled_back):(?<version>[A-Za-z0-9._+-]{1,64})"
                    + @"|(?<result>failed|rollback_failed):(?<version>[A-Za-z0-9._+-]{1,64}):(?<reason>[A-Za-z0-9._+-]{1,64}))\n?\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex LineRegex();

    // Null when the file is missing, a link, not a small regular file, or not one line in the apply step's format.
    public static UpdateResultDto? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            // A fifo or device reports length 0, so this also keeps the open below from blocking.
            if (!info.Exists || info.LinkTarget is not null || info.Length is 0 or > MAX_BYTES)
                return null;

            Span<byte> buffer = stackalloc byte[MAX_BYTES + 1];
            int read;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1))
                read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            return read > MAX_BYTES ? null : Parse(Encoding.ASCII.GetString(buffer[..read]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not read {Path}", path);
            return null;
        }
    }

    public static UpdateResultDto? Parse(string text)
    {
        var match = LineRegex().Match(text);
        if (!match.Success)
            return null;

        var reason = match.Groups["reason"];
        return new(match.Groups["result"].Value, match.Groups["version"].Value, reason.Success ? reason.Value : null);
    }
}

public sealed record LastUpdate(UpdateResultDto? Result);
