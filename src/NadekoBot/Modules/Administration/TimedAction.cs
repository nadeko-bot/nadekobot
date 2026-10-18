using System.Net;
using Discord.Net;

namespace NadekoBot.Modules.Administration;

// Shared rules for the loops which end a timed action (temp role, unban) when its time comes.
public static class TimedAction
{
    // A loop waits at most this long, then reads the next expiry again. Task.Delay refuses waits over ~49.7 days.
    public static readonly TimeSpan MaxWait = TimeSpan.FromDays(1);

    // A row whose action failed for a reason which can go away waits this long before the next try.
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    public static TimeSpan GetWait(DateTime due, DateTime now)
    {
        var wait = due - now;
        if (wait <= TimeSpan.Zero)
            return TimeSpan.Zero;

        return wait > MaxWait ? MaxWait : wait;
    }

    // 403, 404 and other client errors repeat on every try, so the caller drops the row.
    public static bool ShouldRetry(Exception ex)
        => ex switch
        {
            HttpException http => (int)http.HttpCode >= 500 || http.HttpCode == HttpStatusCode.TooManyRequests,
            RateLimitedException or TimeoutException or OperationCanceledException or HttpRequestException => true,
            _ => false
        };
}
