#nullable enable
using Discord.Rest;

namespace NadekoBot.Common;

// The self-hosted program reads the application's flags itself, because Discord closes the gateway (4014) when the bot asks for an intent which the application does not have.
public static class SelfHostIntents
{
    public const int EXIT_DISALLOWED_INTENTS = 104;
    public const int DISALLOWED_INTENTS_CLOSE_CODE = 4014;

    // A service manager which starts the bot again after each exit makes at most 4 logins an hour.
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan _readTimeout = TimeSpan.FromSeconds(15);

    // Null when Discord did not say which privileged intents the application has.
    public static async Task<IntentState?> ReadAsync(string token)
    {
        using var cts = new CancellationTokenSource(_readTimeout);
        try
        {
            await using var rest = new DiscordRestClient();
            await rest.LoginAsync(TokenType.Bot, token).WaitAsync(cts.Token);
            var app = await rest.GetApplicationInfoAsync(new RequestOptions { CancelToken = cts.Token });
            return IntentState.FromApplicationFlags((long)app.Flags);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not read the privileged intents of the application from Discord: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    // The next start asks for fewer intents only when the start knew the flags; else an at-once exit can loop.
    public static bool ExitsAtOnce(IntentState asked, bool startKnewFlags, IntentState? flags)
        => startKnewFlags && flags is not null && flags.LacksAnyOf(asked);
}
