using System.Text.Json;
using NadekoBot.Modules.Searches.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

// Shared checks of the feed and stream routes. The routes act as the bot owner, so a message keeps its mentions.
public static class NotificationInput
{
    public const int MAX_URL_LENGTH = 2048;
    public const int MAX_MESSAGE_LENGTH = DiscordConfig.MaxMessageSize;

    public static bool IsMessage(string? message)
        => message is null || message.Length <= MAX_MESSAGE_LENGTH;

    // A missing field leaves the message as it is; null or an empty string clears it.
    public static bool TryReadMessage(JsonElement value, out bool set, out string? message)
    {
        set = false;
        message = null;
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
                return true;
            case JsonValueKind.Null:
                set = true;
                return true;
            case JsonValueKind.String:
                var text = value.GetString()!;
                if (text.Length > MAX_MESSAGE_LENGTH)
                    return false;

                set = true;
                message = text.Length == 0 ? null : text;
                return true;
            default:
                return false;
        }
    }

    // A text or news channel of the server in which the bot can post an embed.
    public static IResult? CheckPostChannel(SocketGuild guild, string? channelId, out ulong id)
    {
        if (!ApiLimits.TryParseId(channelId, out id)
            || guild.GetChannel(id) is not { } channel
            || ApiEndpoints.Classify(channel) != ApiEndpoints.ChannelKind.Text)
            return ApiErrors.ChannelNotFound;

        if (guild.CurrentUser is not { } me)
            return ApiErrors.NotReady;

        return FeedsService.CanPostIn(me, channel) ? null : ApiErrors.MissingPermissions;
    }

    public static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}
