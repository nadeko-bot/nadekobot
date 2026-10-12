using System.Runtime.CompilerServices;
using System.Text;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.NadekoExpressions;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class ExpressionEndpoints
{
    public const int PAGE_SIZE = 20;

    private const string YAML_CONTENT_TYPE = "text/yaml";
    private const string BOT_MENTION_PLACEHOLDER = "%bot.mention%";

    private static readonly Comparison<NadekoExpression> _byTrigger = static (a, b) =>
    {
        var byTrigger = string.Compare(a.Trigger, b.Trigger, StringComparison.InvariantCultureIgnoreCase);
        return byTrigger != 0 ? byTrigger : a.Id.CompareTo(b.Id);
    };

    // The service replaces its array on every change, so a sorted copy stays valid for as long as its source.
    private static readonly ConditionalWeakTable<NadekoExpression[], NadekoExpression[]> _sorted = new();

    public static RouteGroupBuilder MapExpressionEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/expressions", GetGuildExpressions);
        guild.MapGet("/expressions/export", ExportGuildExpressions);
        guild.MapPost("/expressions", AddGuildExpression);
        guild.MapPut("/expressions/{id:int}", EditGuildExpression);
        guild.MapDelete("/expressions/{id:int}", DeleteGuildExpression);
        guild.MapDelete("/expressions", DeleteAllGuildExpressions);
        return guild;
    }

    public static RouteGroupBuilder MapGlobalExpressionEndpoints(this RouteGroupBuilder owner)
    {
        owner.MapGet("/expressions", GetGlobalExpressions);
        owner.MapGet("/expressions/export", ExportGlobalExpressions);
        owner.MapPost("/expressions", AddGlobalExpression);
        owner.MapPut("/expressions/{id:int}", EditGlobalExpression);
        owner.MapDelete("/expressions/{id:int}", DeleteGlobalExpression);
        return owner;
    }

    private static IResult GetGuildExpressions(
        GuildRef guild,
        int? page,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
        => ListExpressionsInternal(guild.Id, page, svc, client);

    private static IResult GetGlobalExpressions(int? page, NadekoExpressionsService svc, DiscordSocketClient client)
        => ListExpressionsInternal(null, page, svc, client);

    private static IResult ListExpressionsInternal(
        ulong? guildId,
        int? page,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
        => Results.Json(
            BuildExprList(svc.GetExpressionsFor(guildId), ApiLimits.Page(page, 0), client.CurrentUser.Mention),
            ApiJsonContext.Default.ExprListDto);

    private static IResult ExportGuildExpressions(GuildRef guild, NadekoExpressionsService svc)
        => Results.Text(svc.ExportExpressions(guild.Id), YAML_CONTENT_TYPE, Encoding.UTF8);

    private static IResult ExportGlobalExpressions(NadekoExpressionsService svc)
        => Results.Text(svc.ExportExpressions(null), YAML_CONTENT_TYPE, Encoding.UTF8);

    private static Task<IResult> AddGuildExpression(
        GuildRef guild,
        CreateExprRequest body,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
        => AddExpressionInternal(guild.Id, body, svc, client);

    private static Task<IResult> AddGlobalExpression(
        CreateExprRequest body,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
        => AddExpressionInternal(null, body, svc, client);

    private static async Task<IResult> AddExpressionInternal(
        ulong? guildId,
        CreateExprRequest body,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
    {
        if (!ApiLimits.IsText(body.Trigger, ApiLimits.MAX_KEYWORD_LENGTH)
            || !ApiLimits.IsText(body.Response, ApiLimits.MAX_TEXT_LENGTH))
            return ApiErrors.InvalidValue;

        var expr = await svc.AddAsync(
            guildId,
            body.Trigger,
            body.Response,
            body.Ca ?? false,
            body.Ad ?? false,
            body.Dm ?? false);

        return Results.Json(ToExprDto(expr, client.CurrentUser.Mention), ApiJsonContext.Default.ExprDto);
    }

    private static Task<IResult> EditGuildExpression(
        GuildRef guild,
        int id,
        UpdateExprRequest body,
        NadekoExpressionsService svc,
        DbService db,
        DiscordSocketClient client)
        => EditExpressionInternal(guild.Guild, id, body, svc, db, client);

    private static Task<IResult> EditGlobalExpression(
        int id,
        UpdateExprRequest body,
        NadekoExpressionsService svc,
        DbService db,
        DiscordSocketClient client)
        => EditExpressionInternal(null, id, body, svc, db, client);

    private static async Task<IResult> EditExpressionInternal(
        SocketGuild? guild,
        int id,
        UpdateExprRequest body,
        NadekoExpressionsService svc,
        DbService db,
        DiscordSocketClient client)
    {
        if (!ApiLimits.IsOptionalText(body.Response, ApiLimits.MAX_TEXT_LENGTH))
            return ApiErrors.InvalidValue;

        string[]? reactions = null;
        if (body.Reactions is not null && !TryParseReactions(body.Reactions, client, guild, out reactions))
            return ApiErrors.InvalidValue;

        var guildId = guild?.Id;
        var current = await ReadExpressionInternalAsync(db, guildId, id);
        if (current is null)
            return ApiErrors.NotFound;

        var updated = current;
        if (body.Response is not null || body.Ca is not null || body.Ad is not null || body.Dm is not null)
        {
            updated = await svc.EditAsync(guildId,
                id,
                body.Response ?? current.Response ?? string.Empty,
                body.Ca,
                body.Ad,
                body.Dm);
            if (updated is null)
                return ApiErrors.NotFound;
        }

        // ResetExprReactions writes only the database; this also updates the cache the bot reacts from.
        if (reactions is not null)
        {
            await svc.SetExprReactions(guildId, id, reactions);
            updated = await ReadExpressionInternalAsync(db, guildId, id);
            if (updated is null)
                return ApiErrors.NotFound;
        }

        return Results.Json(ToExprDto(updated, client.CurrentUser.Mention), ApiJsonContext.Default.ExprDto);
    }

    private static Task<IResult> DeleteGuildExpression(
        GuildRef guild,
        int id,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
        => DeleteExpressionInternal(guild.Id, id, svc, client);

    private static Task<IResult> DeleteGlobalExpression(int id, NadekoExpressionsService svc, DiscordSocketClient client)
        => DeleteExpressionInternal(null, id, svc, client);

    private static async Task<IResult> DeleteExpressionInternal(
        ulong? guildId,
        int id,
        NadekoExpressionsService svc,
        DiscordSocketClient client)
    {
        var deleted = await svc.DeleteAsync(guildId, id);
        return deleted is null
            ? ApiErrors.NotFound
            : Results.Json(ToExprDto(deleted, client.CurrentUser.Mention), ApiJsonContext.Default.ExprDto);
    }

    // Only the service clears the bot's expression cache, and its call is synchronous EF work.
    private static async Task<IResult> DeleteAllGuildExpressions(GuildRef guild, NadekoExpressionsService svc)
    {
        var count = await Task.Run(() => svc.DeleteAllExpressions(guild.Id));
        return Results.Json(new CountDto(count), ApiJsonContext.Default.CountDto);
    }

    // Mirrors .exprreact: a few emojis or server emotes, stored joined by a separator they must not contain.
    public static bool TryParseReactions(
        IReadOnlyList<string> input,
        DiscordSocketClient client,
        SocketGuild? home,
        out string[] reactions)
    {
        reactions = [];
        if (input.Count > ApiLimits.MAX_REACTIONS)
            return false;

        var parsed = new string[input.Count];
        Span<ulong> emoteIds = stackalloc ulong[ApiLimits.MAX_REACTIONS];
        var emoteCount = 0;
        for (var i = 0; i < parsed.Length; i++)
        {
            var raw = input[i];
            if (string.IsNullOrWhiteSpace(raw) || raw.Contains(ApiLimits.REACTION_SEPARATOR, StringComparison.Ordinal))
                return false;

            if (Emote.TryParse(raw, out var emote))
            {
                emoteIds[emoteCount++] = emote.Id;
                parsed[i] = emote.ToString();
            }
            else if (Emoji.TryParse(raw, out var emoji))
                parsed[i] = emoji.ToString();
            else
                return false;
        }

        if (!HasEmotesInternal(client, home, emoteIds[..emoteCount]))
            return false;

        reactions = parsed;
        return true;
    }

    // Emotes of other servers fail only when the bot reacts; the own server goes first because it usually has them.
    private static bool HasEmotesInternal(DiscordSocketClient client, SocketGuild? home, Span<ulong> emoteIds)
    {
        var missing = emoteIds.Length;
        if (missing == 0)
            return true;

        if (home is not null && ClearFoundInternal(home, emoteIds, ref missing))
            return true;

        foreach (var guild in client.Guilds)
        {
            if (guild != home && ClearFoundInternal(guild, emoteIds, ref missing))
                return true;
        }

        return false;
    }

    private static bool ClearFoundInternal(SocketGuild guild, Span<ulong> emoteIds, ref int missing)
    {
        foreach (var emote in guild.Emotes)
        {
            var index = emoteIds.IndexOf(emote.Id);
            if (index < 0)
                continue;

            // Several reactions can be the same emote, so clear every copy.
            for (var j = index; j < emoteIds.Length; j++)
            {
                if (emoteIds[j] == emote.Id)
                {
                    emoteIds[j] = 0;
                    missing--;
                }
            }

            if (missing == 0)
                return true;
        }

        return false;
    }

    public static ExprListDto BuildExprList(NadekoExpression[] exprs, int page, string botMention)
    {
        var skip = (long)Math.Max(page, 0) * PAGE_SIZE;
        var take = (int)Math.Clamp(exprs.Length - skip, 0, PAGE_SIZE);
        if (take == 0)
            return new ExprListDto(exprs.Length, []);

        var sorted = _sorted.GetValue(exprs, SortInternal);

        var items = new ExprDto[take];
        for (var i = 0; i < take; i++)
            items[i] = ToExprDto(sorted[skip + i], botMention);

        return new ExprListDto(sorted.Length, items);
    }

    private static async Task<NadekoExpression?> ReadExpressionInternalAsync(DbService db, ulong? guildId, int id)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<NadekoExpression>()
                        .Where(x => x.Id == id && x.GuildId == guildId)
                        .FirstOrDefaultAsyncLinqToDB();
    }

    private static NadekoExpression[] SortInternal(NadekoExpression[] exprs)
    {
        var sorted = (NadekoExpression[])exprs.Clone();
        Array.Sort(sorted, _byTrigger);
        return sorted;
    }

    // Shows the stored form, not the cached copy with the bot's mention; old databases have null columns.
    public static ExprDto ToExprDto(NadekoExpression expr, string botMention)
        => new(
            expr.Id,
            new kwum(expr.Id).ToString(),
            (expr.Trigger ?? string.Empty).Replace(botMention, BOT_MENTION_PLACEHOLDER, StringComparison.Ordinal),
            expr.Response ?? string.Empty,
            expr.AllowTarget,
            expr.ContainsAnywhere,
            expr.DmResponse,
            expr.AutoDeleteTrigger,
            ParseReactions(expr.Reactions));

    public static string[] ParseReactions(string? reactions)
        => string.IsNullOrWhiteSpace(reactions)
            ? []
            : reactions.Split(ApiLimits.REACTION_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
}
