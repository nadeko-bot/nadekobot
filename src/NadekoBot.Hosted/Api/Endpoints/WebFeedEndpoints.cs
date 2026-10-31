using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Searches.Services;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

// Website feeds of a server (.feed, .ytuploadnotif). Each handler checks the channel in the gateway cache, then
// calls a public method which tests can run without a guild.
public static class WebFeedEndpoints
{
    public static RouteGroupBuilder MapWebFeedEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/feeds", GetFeeds);
        guild.MapPost("/feeds", AddFeed);
        guild.MapPut("/feeds/{id:int}", UpdateFeed);
        guild.MapDelete("/feeds/{id:int}", DeleteFeed);
        return guild;
    }

    private static async Task<IResult> GetFeeds(GuildRef guild, FeedsService svc)
    {
        var feeds = await svc.GetFeedsAsync(guild.Id);
        var dtos = new FeedSubDto[feeds.Count];
        for (var i = 0; i < dtos.Length; i++)
            dtos[i] = ToDto(feeds[i]);

        return Results.Json(new FeedListDto(dtos, svc.MaxFeeds), ApiJsonContext.Default.FeedListDto);
    }

    private static async Task<IResult> AddFeed(GuildRef guild, CreateFeedRequest body, FeedsService svc)
    {
        if (!IsFeedUrl(body.Url) || !NotificationInput.IsMessage(body.Message))
            return ApiErrors.InvalidValue;

        if (NotificationInput.CheckPostChannel(guild.Guild, body.ChannelId, out var channelId) is { } error)
            return error;

        return await AddFeedAsync(guild.Id, channelId, body.Url, body.Message, svc);
    }

    public static bool IsFeedUrl([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url)
        => ApiLimits.IsText(url, NotificationInput.MAX_URL_LENGTH)
           && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && UrlExtensions.IsPublicUrl(url.Trim());

    public static async Task<IResult> AddFeedAsync(
        ulong guildId,
        ulong channelId,
        string url,
        string? message,
        FeedsService svc)
    {
        var (feedUrl, unavailable) = await svc.ResolveFeedUrlAsync(url.Trim());
        if (unavailable)
            return ApiErrors.PlatformUnavailable(StreamEndpoints.PLATFORM_YOUTUBE);

        if (feedUrl is null)
            return ApiErrors.FeedUnreadable;

        switch (await svc.CheckNewFeedAsync(feedUrl))
        {
            case FeedCheckResult.Unreadable:
                return ApiErrors.FeedUnreadable;
            case FeedCheckResult.Unavailable:
                return ApiErrors.PlatformUnavailable(StreamEndpoints.PLATFORM_YOUTUBE);
        }

        var (result, sub) = await svc.AddFeedAsync(guildId, channelId, feedUrl, NotificationInput.NullIfEmpty(message));
        return result switch
        {
            FeedAddResult.Success => Results.Json(ToDto(sub!),
                ApiJsonContext.Default.FeedSubDto,
                statusCode: StatusCodes.Status201Created),
            FeedAddResult.Duplicate => ApiErrors.DuplicateEntry,
            FeedAddResult.LimitReached => ApiErrors.LimitReachedWithMax(svc.MaxFeeds),
            _ => ApiErrors.InvalidValue
        };
    }

    private static async Task<IResult> UpdateFeed(GuildRef guild, int id, UpdateFeedRequest body, FeedsService svc)
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

        return await UpdateFeedAsync(guild.Id, id, channelId, setMessage, message, svc);
    }

    public static async Task<IResult> UpdateFeedAsync(
        ulong guildId,
        int id,
        ulong? channelId,
        bool setMessage,
        string? message,
        FeedsService svc)
        => await svc.UpdateFeedAsync(guildId, id, channelId, setMessage, message) is { } updated
            ? Results.Json(ToDto(updated), ApiJsonContext.Default.FeedSubDto)
            : ApiErrors.FeedNotFound;

    private static async Task<IResult> DeleteFeed(GuildRef guild, int id, FeedsService svc)
        => await svc.RemoveFeedAsync(guild.Id, id) ? Results.NoContent() : ApiErrors.FeedNotFound;

    public static FeedSubDto ToDto(FeedSub sub)
        => new(sub.Id,
            sub.Url,
            sub.ChannelId.ToString(),
            NotificationInput.NullIfEmpty(sub.Message),
            sub.FailingSince is { } since ? DateTime.SpecifyKind(since, DateTimeKind.Utc) : null);
}
