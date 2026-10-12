using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NadekoBot.Hosted;

// The services trust Discord to bound command input, so these limits stand in for it on the API.
public static class ApiLimits
{
    public const int MAX_REQUEST_BODY_BYTES = 1024 * 1024;

    // Snowflakes are plain digits; the default parse also takes spaces, a sign and culture separators.
    public static bool TryParseId(string? text, out ulong id)
        => ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);

    // A Nitro message, the longest text a command can carry.
    public const int MAX_TEXT_LENGTH = 4000;
    public const int MAX_KEYWORD_LENGTH = 200;
    public const int MAX_NAME_LENGTH = 100;

    // .exprreact keeps at most 3 reactions.
    public const int MAX_REACTIONS = 3;
    public const string REACTION_SEPARATOR = "@@@";

    public const int MAX_PAGE = 100_000;

    public const int MIN_REWARD_LEVEL = 1;

    // The xp formula overflows a long far above this level.
    public const int MAX_XP_LEVEL = 100_000;

    // .xpadd takes an int.
    public const long MAX_XP_AMOUNT = int.MaxValue;

    public const int MAX_DELETE_TIMER_SECONDS = 3600;

    // .warnexpire
    public const int MAX_WARN_EXPIRE_DAYS = 366;

    public const int DISCORD_CALLS_PER_GUILD = 5;
    public static readonly TimeSpan DiscordCallWindow = TimeSpan.FromMinutes(1);

    public static bool IsText([NotNullWhen(true)] string? value, int maxLength)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;

    public static bool IsOptionalText(string? value, int maxLength)
        => value is null || IsText(value, maxLength);

    public static int Page(int? page, int first)
        => Math.Clamp(page ?? first, first, MAX_PAGE);
}
