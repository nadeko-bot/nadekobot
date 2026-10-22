using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Modules.Owner.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class PresenceEndpoints
{
    private const string ONLINE = "online";
    private const string IDLE = "idle";
    private const string DND = "dnd";
    private const string INVISIBLE = "invisible";

    private const string PLAYING = "playing";
    private const string LISTENING = "listening";
    private const string WATCHING = "watching";
    private const string COMPETING = "competing";
    private const string CUSTOM = "custom";
    private const string STREAMING = "streaming";

    public static RouteGroupBuilder MapPresenceEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/presence", GetPresence);
        owner.MapPut("/presence", SetPresence).RequireRateLimiting(ApiHost.PRESENCE_POLICY);
        return owner;
    }

    private static IResult GetPresence(BotActivityService activity)
        => Results.Json(ToDto(activity.GetPresence()), ApiJsonContext.Default.PresenceDto);

    // The commands write the same saved presence, so both always show the same value.
    private static async Task<IResult> SetPresence(SetPresenceRequest body, BotActivityService activity)
    {
        if (!TryParseStatus(body.Status, out var status))
            return ApiErrors.InvalidValue;

        ActivityType? type = null;
        if (body.Activity is { } a)
        {
            if (!TryParseType(a.Type, out var parsed))
                return ApiErrors.InvalidValue;

            type = parsed;
        }

        var error = await activity.SetPresenceAsync(status, type, body.Activity?.Text, body.Activity?.Url);
        if (error != PresenceError.None)
            return ApiErrors.InvalidValue;

        Log.Information("The bot's presence was set over the API");
        return Results.Json(ToDto(activity.GetPresence()), ApiJsonContext.Default.PresenceDto);
    }

    private static PresenceDto ToDto(in BotPresenceState state)
        => new(StatusName(state.Status),
            state.Activity is { } a ? new PresenceActivityDto(TypeName(a.Type), a.Text, a.Url) : null,
            state.Rotating);

    private static bool TryParseStatus(string? value, out UserStatus status)
    {
        status = value switch
        {
            ONLINE => UserStatus.Online,
            IDLE => UserStatus.Idle,
            DND => UserStatus.DoNotDisturb,
            INVISIBLE => UserStatus.Invisible,
            _ => UserStatus.Offline
        };

        return status != UserStatus.Offline;
    }

    private static bool TryParseType(string? value, out ActivityType type)
    {
        switch (value)
        {
            case PLAYING:
                type = ActivityType.Playing;
                return true;
            case LISTENING:
                type = ActivityType.Listening;
                return true;
            case WATCHING:
                type = ActivityType.Watching;
                return true;
            case COMPETING:
                type = ActivityType.Competing;
                return true;
            case CUSTOM:
                type = ActivityType.CustomStatus;
                return true;
            case STREAMING:
                type = ActivityType.Streaming;
                return true;
            default:
                type = default;
                return false;
        }
    }

    // GetPresence gives only the four settable statuses and the six known types.
    private static string StatusName(UserStatus status)
        => status switch
        {
            UserStatus.Idle => IDLE,
            UserStatus.DoNotDisturb => DND,
            UserStatus.Invisible => INVISIBLE,
            _ => ONLINE
        };

    private static string TypeName(ActivityType type)
        => type switch
        {
            ActivityType.Listening => LISTENING,
            ActivityType.Watching => WATCHING,
            ActivityType.Competing => COMPETING,
            ActivityType.CustomStatus => CUSTOM,
            ActivityType.Streaming => STREAMING,
            _ => PLAYING
        };
}
