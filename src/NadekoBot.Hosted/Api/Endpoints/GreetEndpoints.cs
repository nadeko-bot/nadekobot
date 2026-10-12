using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using System.Net;
using Discord.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class GreetEndpoints
{
    private const string GREET = "greet";
    private const string GREET_DM = "greetdm";
    private const string BYE = "bye";
    private const string BOOST = "boost";

    private static readonly GreetType[] _types = Enum.GetValues<GreetType>();

    public static RouteGroupBuilder MapGreetEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/greets", GetGreets);
        guild.MapPut("/greets/{type}", UpdateGreet);
        guild.MapPost("/greets/{type}/test", TestGreet).RequireRateLimiting(ApiHost.DISCORD_CALL_POLICY);
        return guild;
    }

    private static async Task<IResult> GetGreets(GuildRef guild, DbService db)
        => Results.Json(await ReadGreetDtosAsync(db, guild.Id), ApiJsonContext.Default.GreetDtoArray);

    // Every greet type, with defaults for the ones the server never set.
    public static async Task<GreetDto[]> ReadGreetDtosAsync(DbService db, ulong guildId)
    {
        var rows = await ReadGreetsInternalAsync(db, guildId);
        var arr = new GreetDto[_types.Length];
        for (var i = 0; i < _types.Length; i++)
            arr[i] = ToGreetDto(_types[i], FindInternal(rows, _types[i]));

        return arr;
    }

    // Service setters also update the bot's greet state; as with .greet, concurrent edits end with the last one.
    private static async Task<IResult> UpdateGreet(
        GuildRef guildRef,
        string type,
        UpdateGreetRequest body,
        GreetService svc,
        DbService db)
    {
        if (!TryParseGreetType(type, out var greetType))
            return ApiErrors.NotFound;

        if (body.AutoDeleteTimer is { } timer && !IsValidTimer(timer))
            return ApiErrors.InvalidValue;

        if (body.Message is not null && !ApiLimits.IsText(body.Message, ApiLimits.MAX_TEXT_LENGTH))
            return ApiErrors.InvalidValue;

        var guild = guildRef.Guild;
        var setGreet = body.Enabled is not null || body.ChannelId is not null;
        ulong? channelId = null;
        var enabled = false;

        // Everything is checked before the first write, so a request which fails validation changes nothing.
        if (setGreet || body.Message is not null)
        {
            var current = await ReadGreetInternalAsync(db, guild.Id, greetType);

            channelId = current?.ChannelId;
            if (body.ChannelId is not null)
            {
                if (!ApiLimits.TryParseId(body.ChannelId, out var parsed))
                    return ApiErrors.ChannelNotFound;
                channelId = parsed;
            }

            enabled = body.Enabled ?? current?.IsEnabled ?? false;

            var channel = channelId is { } id ? FindMessageChannelInternal(guild, id) : null;
            if (CheckGreetChannel(greetType, channelId, body.ChannelId is not null, enabled, channel is not null) is { } error)
                return error;

            // The bot turns a greet off at the first join it cannot post; this says so now instead.
            var message = body.Message ?? current?.MessageText ?? GreetService.GetDefaultGreet(greetType);
            if (enabled
                && greetType != GreetType.GreetDm
                && channel is not null
                && !CanSendInternal(guild.CurrentUser.GetPermissions(channel), message))
                return ApiErrors.MissingPermissions;
        }

        if (body.Message is not null)
            await svc.SetMessage(guild.Id, greetType, body.Message);

        if (body.AutoDeleteTimer is { } t)
            await svc.SetDeleteTimer(guild.Id, greetType, t);

        if (setGreet)
            await svc.SetGreet(guild.Id, channelId, greetType, enabled);

        var updated = await ReadGreetInternalAsync(db, guild.Id, greetType);
        return Results.Json(ToGreetDto(greetType, updated), ApiJsonContext.Default.GreetDto);
    }

    // The stored channel is checked too: it may have been deleted, and the bot would turn the greet off.
    public static IResult? CheckGreetChannel(
        GreetType type,
        ulong? channelId,
        bool channelGiven,
        bool enabled,
        bool channelUsable)
    {
        var needsChannel = enabled && type != GreetType.GreetDm;
        if (needsChannel && channelId is null)
            return ApiErrors.ChannelRequired;

        if ((channelGiven || needsChannel) && channelId is not null && !channelUsable)
            return ApiErrors.ChannelNotFound;

        return null;
    }

    private static async Task<IResult> TestGreet(
        GuildRef guildRef,
        string type,
        TestGreetRequest? body,
        GreetService svc,
        DbService db,
        DiscordSocketClient client)
    {
        if (!TryParseGreetType(type, out var greetType))
            return ApiErrors.NotFound;

        var guild = guildRef.Guild;
        var userId = guild.OwnerId;
        if (body?.UserId is { } rawUserId && !ApiLimits.TryParseId(rawUserId, out userId))
            return ApiErrors.InvalidValue;

        var conf = await ReadGreetInternalAsync(db, guild.Id, greetType);

        SocketTextChannel? channel;
        if (body?.ChannelId is { } rawChannelId)
        {
            if (!ApiLimits.TryParseId(rawChannelId, out var channelId)
                || FindMessageChannelInternal(guild, channelId) is not { } picked)
                return ApiErrors.ChannelNotFound;
            channel = picked;
        }
        else
        {
            channel = (conf?.ChannelId is { } confChannelId ? FindMessageChannelInternal(guild, confChannelId) : null)
                      ?? FirstMessageChannelInternal(guild);
        }

        // A dm greet sends no channel message, but the service still needs a channel for a missing settings row.
        if (channel is null)
            return ApiErrors.ChannelRequired;

        // A send which fails for permissions turns the greet off; the check uses the cached settings the test sends.
        var sent = await svc.GetGreetSettingsAsync(guild.Id, greetType);
        var message = sent?.MessageText ?? GreetService.GetDefaultGreet(greetType);
        if (greetType != GreetType.GreetDm && !CanSendInternal(guild.CurrentUser.GetPermissions(channel), message))
            return ApiErrors.MissingPermissions;

        // Large servers do not cache every member, often not even the owner; the route's rate limit covers this call.
        IGuildUser? user = guild.GetUser(userId);
        try
        {
            user ??= await client.Rest.GetGuildUserAsync(guild.Id, userId);
        }
        catch (HttpException ex)
        {
            Log.Warning("Fetching member {UserId} of guild {GuildId} for a greet test failed with {Status}",
                userId,
                guild.Id,
                ex.HttpCode);
            return ex.HttpCode == HttpStatusCode.Forbidden ? ApiErrors.MissingPermissions : ApiErrors.DiscordUnavailable;
        }
        catch (TimeoutException ex)
        {
            Log.Warning(ex, "Fetching member {UserId} of guild {GuildId} for a greet test timed out", userId, guild.Id);
            return ApiErrors.DiscordUnavailable;
        }

        if (user is null)
            return ApiErrors.UserNotFound;

        await svc.Test(guild.Id, greetType, channel, user);

        var after = await ReadGreetInternalAsync(db, guild.Id, greetType);
        return Results.Json(ToGreetDto(greetType, after), ApiJsonContext.Default.GreetDto);
    }

    // The greet service caches settings and its setters do not clear the cache, so the API reads the table.
    private static async Task<GreetSettings[]> ReadGreetsInternalAsync(DbService db, ulong guildId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<GreetSettings>()
                        .Where(x => x.GuildId == guildId)
                        .ToArrayAsyncLinqToDB();
    }

    private static async Task<GreetSettings?> ReadGreetInternalAsync(DbService db, ulong guildId, GreetType type)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<GreetSettings>()
                        .Where(x => x.GuildId == guildId && x.GreetType == type)
                        .FirstOrDefaultAsyncLinqToDB();
    }

    private static GreetSettings? FindInternal(GreetSettings[] rows, GreetType type)
    {
        foreach (var row in rows)
        {
            if (row.GreetType == type)
                return row;
        }

        return null;
    }

    // A greet goes only to a plain text channel, never to a thread, voice or stage channel.
    private static SocketTextChannel? FindMessageChannelInternal(SocketGuild guild, ulong channelId)
        => guild.GetTextChannel(channelId) is { } channel && ApiEndpoints.Classify(channel) == ApiEndpoints.ChannelKind.Text
            ? channel
            : null;

    // The plain text channel at the top of the list which the bot can see, as Discord's default channel.
    private static SocketTextChannel? FirstMessageChannelInternal(SocketGuild guild)
    {
        SocketTextChannel? first = null;
        foreach (var channel in guild.TextChannels)
        {
            if (ApiEndpoints.Classify(channel) != ApiEndpoints.ChannelKind.Text
                || !guild.CurrentUser.GetPermissions(channel).ViewChannel)
                continue;

            if (first is null || channel.Position < first.Position)
                first = channel;
        }

        return first;
    }

    private static bool CanSendInternal(ChannelPermissions perms, string message)
        => perms.ViewChannel
           && perms.SendMessages
           && (perms.EmbedLinks || SmartText.CreateFrom(message).IsPlainText);

    public static bool TryParseGreetType(string type, out GreetType greetType)
    {
        if (string.Equals(type, GREET, StringComparison.InvariantCultureIgnoreCase))
            greetType = GreetType.Greet;
        else if (string.Equals(type, GREET_DM, StringComparison.InvariantCultureIgnoreCase))
            greetType = GreetType.GreetDm;
        else if (string.Equals(type, BYE, StringComparison.InvariantCultureIgnoreCase))
            greetType = GreetType.Bye;
        else if (string.Equals(type, BOOST, StringComparison.InvariantCultureIgnoreCase))
            greetType = GreetType.Boost;
        else
        {
            greetType = default;
            return false;
        }

        return true;
    }

    public static bool IsValidTimer(int timer)
        => timer is >= 0 and <= ApiLimits.MAX_DELETE_TIMER_SECONDS;

    public static GreetDto ToGreetDto(GreetType type, GreetSettings? conf)
        => new(
            GreetTypeNameInternal(type),
            conf?.IsEnabled ?? false,
            conf?.ChannelId?.ToString(),
            conf?.MessageText ?? GreetService.GetDefaultGreet(type),
            conf?.AutoDeleteTimer ?? 0);

    private static string GreetTypeNameInternal(GreetType type)
        => type switch
        {
            GreetType.Greet => GREET,
            GreetType.GreetDm => GREET_DM,
            GreetType.Bye => BYE,
            GreetType.Boost => BOOST,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
}
