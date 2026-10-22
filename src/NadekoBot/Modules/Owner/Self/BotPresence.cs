using System.Text.RegularExpressions;
using NadekoBot.Common.Configs;

namespace NadekoBot.Modules.Owner.Services;

public enum PresenceError
{
    None,
    Status,
    Type,
    Text,
    Url
}

public readonly record struct BotPresenceState(UserStatus Status, PresenceActivity? Activity, bool Rotating);

public static partial class BotPresence
{
    public const int MAX_TEXT_LENGTH = 128;
    public const int MAX_URL_LENGTH = 512;

    // Discord shows a stream only for a Twitch or YouTube page. NadeKloud checks links with the same pattern.
    [GeneratedRegex(@"^https://(?:www\.|m\.)?(?:twitch\.tv|youtube\.com)/\S+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreamUrlRegex();

    public static bool IsSettableStatus(UserStatus status)
        => status is UserStatus.Online or UserStatus.Idle or UserStatus.DoNotDisturb or UserStatus.Invisible;

    public static bool IsKnownType(ActivityType type)
        => type is ActivityType.Playing
            or ActivityType.Streaming
            or ActivityType.Listening
            or ActivityType.Watching
            or ActivityType.CustomStatus
            or ActivityType.Competing;

    public static bool IsStreamUrl(string? url)
        => url is { Length: > 0 and <= MAX_URL_LENGTH } && StreamUrlRegex().IsMatch(url);

    public static PresenceError Check(ActivityType type, ReadOnlySpan<char> trimmedText, string? url)
    {
        if (!IsKnownType(type))
            return PresenceError.Type;

        if (trimmedText.Length is 0 or > MAX_TEXT_LENGTH)
            return PresenceError.Text;

        if (type == ActivityType.Streaming ? !IsStreamUrl(url) : url is not null)
            return PresenceError.Url;

        return PresenceError.None;
    }

    public static PresenceError TryCreateActivity(
        ActivityType type,
        string? text,
        string? url,
        out PresenceActivity? activity)
    {
        var trimmed = text.AsSpan().Trim();
        var error = Check(type, trimmed, url);
        activity = error == PresenceError.None
            ? new()
            {
                Type = type,
                Text = trimmed.Length == text!.Length ? text : trimmed.ToString(),
                Url = url
            }
            : null;

        return error;
    }

    // A hand-edited bot.yml can hold anything, so each reader gets a value which Discord accepts.
    public static (UserStatus Status, PresenceActivity? Activity) Normalize(PresenceConfig? presence)
    {
        if (presence is null)
            return (UserStatus.Online, null);

        var status = IsSettableStatus(presence.Status) ? presence.Status : UserStatus.Online;
        var activity = presence.Activity;
        if (activity is null || Check(activity.Type, activity.Text.AsSpan().Trim(), activity.Url) != PresenceError.None)
            return (status, null);

        return (status, activity);
    }
}
