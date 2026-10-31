using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;
using FollowResult = NadekoBot.Modules.Searches.Services.StreamNotificationService.FollowStreamResult;
using StreamUpdate = NadekoBot.Modules.Searches.Services.StreamNotificationService.UpdateStreamResult;

namespace NadekoBot.Hosted.Endpoints;

// Followed streams of a server (.stadd, .streamremove, .streammessage, .stoff, .stondel).
public static class StreamEndpoints
{
    public const string PLATFORM_TWITCH = "twitch";
    public const string PLATFORM_PICARTO = "picarto";
    public const string PLATFORM_YOUTUBE = "youtube";
    public const string PLATFORM_FACEBOOK = "facebook";
    public const string PLATFORM_TROVO = "trovo";
    public const string PLATFORM_KICK = "kick";

    public static RouteGroupBuilder MapStreamEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/streams", GetStreams);
        guild.MapPost("/streams", AddStream);
        guild.MapPut("/streams/settings", UpdateSettings);
        guild.MapPut("/streams/{id:int}", UpdateStream);
        guild.MapDelete("/streams/{id:int}", DeleteStream);
        return guild;
    }

    private static async Task<IResult> GetStreams(GuildRef guild, StreamNotificationService svc)
    {
        var streams = await svc.GetStreamsAsync(guild.Id);
        var (notifyOffline, deleteOnline) = await svc.GetStreamSettingsAsync(guild.Id);
        var dtos = new FollowedStreamDto[streams.Count];
        for (var i = 0; i < dtos.Length; i++)
            dtos[i] = ToDto(streams[i]);

        return Results.Json(new StreamListDto(dtos, svc.MaxFollowedStreams, notifyOffline, deleteOnline),
            ApiJsonContext.Default.StreamListDto);
    }

    private static async Task<IResult> AddStream(GuildRef guild, CreateStreamRequest body, StreamNotificationService svc)
    {
        if (!ApiLimits.IsText(body.Url, NotificationInput.MAX_URL_LENGTH) || !NotificationInput.IsMessage(body.Message))
            return ApiErrors.InvalidValue;

        if (NotificationInput.CheckPostChannel(guild.Guild, body.ChannelId, out var channelId) is { } error)
            return error;

        return await AddStreamAsync(guild.Id, channelId, body.Url, body.Message, svc);
    }

    public static async Task<IResult> AddStreamAsync(
        ulong guildId,
        ulong channelId,
        string url,
        string? message,
        StreamNotificationService svc)
    {
        var outcome = await svc.FollowStream(guildId, channelId, url.Trim(), NotificationInput.NullIfEmpty(message));
        return outcome.Result switch
        {
            FollowResult.Followed => Results.Json(ToDto(outcome.Stream),
                ApiJsonContext.Default.FollowedStreamDto,
                statusCode: StatusCodes.Status201Created),
            FollowResult.AlreadyFollowed => ApiErrors.DuplicateEntry,
            FollowResult.LimitReached => ApiErrors.LimitReachedWithMax(svc.MaxFollowedStreams),
            FollowResult.Unsupported => ApiErrors.UnsupportedLink,
            FollowResult.Unavailable => ApiErrors.PlatformUnavailable(PlatformName(outcome.Platform ?? FollowedStream.FType.Twitch)),
            _ => ApiErrors.StreamUnknown
        };
    }

    private static async Task<IResult> UpdateStream(
        GuildRef guild,
        int id,
        UpdateStreamRequest body,
        StreamNotificationService svc)
    {
        if (!NotificationInput.TryReadMessage(body.Message, out var setMessage, out var message))
            return ApiErrors.InvalidValue;

        ulong? channelId = null;
        if (body.ChannelId is not null)
        {
            if (NotificationInput.CheckPostChannel(guild.Guild, body.ChannelId, out var newChannel) is { } error)
                return error;

            channelId = newChannel;
        }

        return await UpdateStreamAsync(guild.Id, id, channelId, setMessage, message, svc);
    }

    public static async Task<IResult> UpdateStreamAsync(
        ulong guildId,
        int id,
        ulong? channelId,
        bool setMessage,
        string? message,
        StreamNotificationService svc)
    {
        var (result, stream) = await svc.UpdateStreamAsync(guildId, id, channelId, setMessage, message);
        return result switch
        {
            StreamUpdate.Updated => Results.Json(ToDto(stream), ApiJsonContext.Default.FollowedStreamDto),
            StreamUpdate.Duplicate => ApiErrors.DuplicateEntry,
            _ => ApiErrors.StreamNotFound
        };
    }

    private static async Task<IResult> DeleteStream(GuildRef guild, int id, StreamNotificationService svc)
        => await svc.UnfollowStreamByIdAsync(guild.Id, id) is null ? ApiErrors.StreamNotFound : Results.NoContent();

    private static Task<IResult> UpdateSettings(
        GuildRef guild,
        UpdateStreamSettingsRequest body,
        StreamNotificationService svc)
        => UpdateSettingsAsync(guild.Id, body, svc);

    public static async Task<IResult> UpdateSettingsAsync(
        ulong guildId,
        UpdateStreamSettingsRequest body,
        StreamNotificationService svc)
    {
        var (notifyOffline, deleteOnline) =
            await svc.SetStreamSettingsAsync(guildId, body.NotifyOffline, body.DeleteOnlineMessage);
        return Results.Json(new StreamSettingsDto(notifyOffline, deleteOnline), ApiJsonContext.Default.StreamSettingsDto);
    }

    public static FollowedStreamDto ToDto(FollowedStream fs)
        => new(fs.Id,
            PlatformName(fs.Type),
            string.IsNullOrWhiteSpace(fs.PrettyName) ? fs.Username : fs.PrettyName,
            StreamUrl(fs.Type, fs.Username),
            fs.ChannelId.ToString(),
            NotificationInput.NullIfEmpty(fs.Message));

    public static string PlatformName(FollowedStream.FType type)
        => type switch
        {
            FollowedStream.FType.Twitch => PLATFORM_TWITCH,
            FollowedStream.FType.Picarto => PLATFORM_PICARTO,
            FollowedStream.FType.Youtube => PLATFORM_YOUTUBE,
            FollowedStream.FType.Facebook => PLATFORM_FACEBOOK,
            FollowedStream.FType.Trovo => PLATFORM_TROVO,
            _ => PLATFORM_KICK
        };

    public static string StreamUrl(FollowedStream.FType type, string name)
    {
        var escaped = Uri.EscapeDataString(name);
        return type switch
        {
            FollowedStream.FType.Twitch => "https://twitch.tv/" + escaped,
            FollowedStream.FType.Picarto => "https://picarto.tv/" + escaped,
            FollowedStream.FType.Youtube => "https://www.youtube.com/channel/" + escaped,
            FollowedStream.FType.Facebook => "https://facebook.com/" + escaped,
            FollowedStream.FType.Trovo => "https://trovo.live/" + escaped,
            _ => "https://kick.com/" + escaped
        };
    }
}
