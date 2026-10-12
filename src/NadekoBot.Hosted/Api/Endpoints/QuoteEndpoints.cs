using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Utility;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace NadekoBot.Hosted.Endpoints;

public static class QuoteEndpoints
{
    private const string ORDER_BY_ID = "id";
    private const string ORDER_BY_KEYWORD = "keyword";

    public static RouteGroupBuilder MapQuoteEndpoints(this RouteGroupBuilder guild)
    {
        guild.MapGet("/quotes", GetQuotes);
        guild.MapGet("/quotes/search", SearchQuotes);
        guild.MapPost("/quotes", AddQuote);
        guild.MapPut("/quotes/{id:int}", EditQuote);
        guild.MapDelete("/quotes/{id:int}", DeleteQuote);
        guild.MapDelete("/quotes", DeleteQuotesByKeyword);
        return guild;
    }

    private static async Task<IResult> GetQuotes(GuildRef guild, int? page, string? order, IQuoteService svc)
    {
        if (!TryParseOrder(order, out var orderType))
            return ApiErrors.InvalidValue;

        var quotes = await svc.GetAllQuotesAsync(guild.Id, ApiLimits.Page(page, 0), orderType);
        return Results.Json(ToQuoteDtos(quotes), ApiJsonContext.Default.QuoteDtoArray);
    }

    private static async Task<IResult> SearchQuotes(GuildRef guild, string? q, int? page, IQuoteService svc)
    {
        if (!ApiLimits.IsText(q, ApiLimits.MAX_TEXT_LENGTH))
            return ApiErrors.InvalidValue;

        var (quotes, total) = await svc.FindQuotesAsync(guild.Id, q, ApiLimits.Page(page, 1));
        return Results.Json(new QuoteSearchDto(total, ToQuoteDtos(quotes)), ApiJsonContext.Default.QuoteSearchDto);
    }

    private static async Task<IResult> AddQuote(GuildRef guildRef, CreateQuoteRequest body, IQuoteService svc, DbService db)
    {
        if (!ApiLimits.IsText(body.Keyword, ApiLimits.MAX_KEYWORD_LENGTH)
            || !ApiLimits.IsText(body.Text, ApiLimits.MAX_TEXT_LENGTH))
            return ApiErrors.InvalidValue;

        var guild = guildRef.Guild;
        var authorId = guild.CurrentUser.Id;
        var authorName = guild.CurrentUser.Username;
        if (body.AuthorId is not null)
        {
            if (!ApiLimits.TryParseId(body.AuthorId, out authorId))
                return ApiErrors.InvalidValue;

            // A member who left is not in the cache, but the bot still knows names it has seen.
            if ((guild.GetUser(authorId)?.Username ?? await ReadUsernameInternalAsync(db, authorId)) is not { } name)
                return ApiErrors.UserNotFound;
            authorName = name;
        }

        var quote = await svc.AddQuoteAsync(guild.Id, authorId, authorName, body.Keyword, body.Text);
        if (quote is null)
            return ApiErrors.LimitReached;

        return Results.Json(ToQuoteDto(quote), ApiJsonContext.Default.QuoteDto);
    }

    private static async Task<IResult> EditQuote(GuildRef guild, int id, UpdateQuoteRequest body, DbService db)
    {
        if (!ApiLimits.IsOptionalText(body.Keyword, ApiLimits.MAX_KEYWORD_LENGTH)
            || !ApiLimits.IsOptionalText(body.Text, ApiLimits.MAX_TEXT_LENGTH))
            return ApiErrors.InvalidValue;

        var updated = await UpdateQuoteAsync(db, guild.Id, id, body.Keyword, body.Text);
        return updated is null
            ? ApiErrors.NotFound
            : Results.Json(ToQuoteDto(updated), ApiJsonContext.Default.QuoteDto);
    }

    private static async Task<string?> ReadUsernameInternalAsync(DbService db, ulong userId)
    {
        await using var ctx = db.GetDbContext();
        return await ctx.GetTable<DiscordUser>()
                        .Where(x => x.UserId == userId)
                        .Select(x => x.Username)
                        .FirstOrDefaultAsyncLinqToDB();
    }

    // One statement which sets only the given fields, so two edits of different fields both stay.
    public static async Task<Quote?> UpdateQuoteAsync(DbService db, ulong guildId, int id, string? keyword, string? text)
    {
        await using var ctx = db.GetDbContext();
        var quotes = ctx.GetTable<Quote>().Where(x => x.Id == id && x.GuildId == guildId);
        if (keyword is null && text is null)
            return await quotes.FirstOrDefaultAsyncLinqToDB();

        var update = quotes.AsUpdatable();
        // Quote keywords are stored upper case; .quoteadd does the same.
        if (keyword is not null)
            update = update.Set(x => x.Keyword, keyword.ToUpperInvariant());
        if (text is not null)
            update = update.Set(x => x.Text, text);

        var rows = await update.UpdateWithOutputAsync(static (_, inserted) => inserted);
        return rows.Length > 0 ? rows[0] : null;
    }

    private static async Task<IResult> DeleteQuote(GuildRef guild, int id, IQuoteService svc)
        => await svc.DeleteQuoteAsync(guild.Id, 0, isQuoteManager: true, id)
            ? Results.NoContent()
            : ApiErrors.NotFound;

    private static async Task<IResult> DeleteQuotesByKeyword(GuildRef guild, string? keyword, IQuoteService svc)
    {
        if (!ApiLimits.IsText(keyword, ApiLimits.MAX_KEYWORD_LENGTH))
            return ApiErrors.InvalidValue;

        var count = await svc.RemoveAllByKeyword(guild.Id, keyword);
        return Results.Json(new CountDto(count), ApiJsonContext.Default.CountDto);
    }

    public static bool TryParseOrder(string? order, out OrderType orderType)
    {
        orderType = OrderType.Id;
        if (string.IsNullOrWhiteSpace(order) || string.Equals(order, ORDER_BY_ID, StringComparison.InvariantCultureIgnoreCase))
            return true;

        if (!string.Equals(order, ORDER_BY_KEYWORD, StringComparison.InvariantCultureIgnoreCase))
            return false;

        orderType = OrderType.Keyword;
        return true;
    }

    private static QuoteDto[] ToQuoteDtos(IReadOnlyCollection<Quote> quotes)
    {
        var arr = new QuoteDto[quotes.Count];
        var i = 0;
        foreach (var q in quotes)
            arr[i++] = ToQuoteDto(q);

        return arr;
    }

    public static QuoteDto ToQuoteDto(Quote quote)
        => new(
            quote.Id,
            new kwum(quote.Id).ToString(),
            quote.Keyword,
            quote.Text,
            quote.AuthorName,
            quote.AuthorId.ToString());
}
