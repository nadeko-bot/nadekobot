using System.Threading.RateLimiting;

namespace NadekoBot.Hosted;

// Only a request which makes the bot join or leave a voice channel uses a permit, so queue changes have no limit.
public sealed class VoiceChangeLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<ulong> _limiter = PartitionedRateLimiter.Create<ulong, ulong>(
        static guildId => RateLimitPartition.GetFixedWindowLimiter(
            guildId,
            static _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ApiLimits.VOICE_CHANGES_PER_GUILD,
                Window = ApiLimits.VoiceChangeWindow,
                QueueLimit = 0
            }));

    // Null when the server has a permit, else the whole seconds until the next one.
    public int? TryAcquire(ulong guildId)
    {
        using var lease = _limiter.AttemptAcquire(guildId);
        if (lease.IsAcquired)
            return null;

        var wait = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? retryAfter
            : ApiLimits.VoiceChangeWindow;
        return Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
    }

    public void Dispose()
        => _limiter.Dispose();
}
