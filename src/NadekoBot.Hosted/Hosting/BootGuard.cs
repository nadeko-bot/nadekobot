using System.Globalization;

namespace NadekoBot.Hosted;

// Turns a crash loop under the supervisor into health "failed" after MAX_ATTEMPTS starts which never got ready.
public sealed class BootGuard(HostPaths paths, TimeProvider time)
{
    public const int MAX_ATTEMPTS = 3;

    // A failed bot tries again after this long, so an outage of Discord or the network does not need a person.
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    public bool TryBeginAttempt()
    {
        var (attempts, lastAttempt) = ReadInternal();
        var now = time.GetUtcNow();
        if (attempts >= MAX_ATTEMPTS)
        {
            // A last attempt in the future means the clock moved back; waiting for it could take days.
            if (now - lastAttempt < RetryAfter && lastAttempt <= now)
                return false;

            attempts = 0;
        }

        WriteInternal(attempts + 1, now);
        return true;
    }

    public TimeSpan UntilRetry()
    {
        var remaining = ReadInternal().LastAttempt + RetryAfter - time.GetUtcNow();
        return TimeSpan.FromTicks(Math.Clamp(remaining.Ticks, 0, RetryAfter.Ticks));
    }

    public void Reset()
    {
        try
        {
            File.Delete(paths.BootAttempts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not reset {Path}", paths.BootAttempts);
        }
    }

    public void MarkReady()
    {
        Reset();
        KeepUpdate();
    }

    // The apply step rolls back while .update-trial exists, so only a start which works may remove it.
    public void KeepUpdate()
    {
        try
        {
            if (!File.Exists(paths.UpdateTrial))
                return;

            File.Delete(paths.UpdateTrial);
            Log.Information("Version {Version} started. The update is kept", StatsService.BotVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not delete {Path}", paths.UpdateTrial);
        }
    }

    private (int Attempts, DateTimeOffset LastAttempt) ReadInternal()
    {
        try
        {
            if (!File.Exists(paths.BootAttempts))
                return (0, DateTimeOffset.MinValue);

            var text = File.ReadAllText(paths.BootAttempts).AsSpan();
            Span<Range> parts = stackalloc Range[2];
            var count = text.Split(parts, '\n', StringSplitOptions.TrimEntries);
            var attempts = count > 0 && int.TryParse(text[parts[0]], CultureInfo.InvariantCulture, out var n) ? n : 0;
            var last = count > 1 && long.TryParse(text[parts[1]], CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : DateTimeOffset.MinValue;
            return (attempts, last);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            Log.Warning(ex, "Could not read {Path}", paths.BootAttempts);
            return (0, DateTimeOffset.MinValue);
        }
    }

    // A full disk must not crash the process: the API has to stay up so a manager can delete backups.
    private void WriteInternal(int attempts, DateTimeOffset at)
    {
        try
        {
            SecretFile.WriteAllText(paths.BootAttempts,
                string.Create(CultureInfo.InvariantCulture, $"{attempts}\n{at.ToUnixTimeSeconds()}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not write {Path}. This start is not counted", paths.BootAttempts);
        }
    }
}
