#nullable disable warnings
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Common.Yml;
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public sealed class QuoteService : IQuoteService, INService
{
    private readonly DbService _db;

    public QuoteService(DbService db)
    {
        _db = db;
    }

    /// <summary>
    /// Delete all quotes created by the author in a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <returns>Number of deleted qutoes</returns>
    public async Task<int> DeleteAllAuthorQuotesAsync(ulong guildId, ulong userId)
    {
        await using var ctx = _db.GetDbContext();
        var deleted = await ctx.GetTable<Quote>()
                               .Where(x => x.GuildId == guildId && x.AuthorId == userId)
                               .DeleteAsync();

        return deleted;
    }

    /// <summary>
    /// Delete all quotes in a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <returns>Number of deleted qutoes</returns>
    public async Task<int> DeleteAllQuotesAsync(ulong guildId)
    {
        await using var ctx = _db.GetDbContext();
        var deleted = await ctx.GetTable<Quote>()
                               .Where(x => x.GuildId == guildId)
                               .DeleteAsync();

        return deleted;
    }

    public async Task<IReadOnlyCollection<Quote>> GetAllQuotesAsync(ulong guildId, int page, OrderType order)
    {
        await using var uow = _db.GetDbContext();
        var q = uow.Set<Quote>()
                   .ToLinqToDBTable()
                   .Where(x => x.GuildId == guildId);

        if (order == OrderType.Keyword)
            q = q.OrderBy(x => x.Keyword);
        else
            q = q.OrderBy(x => x.Id);

        return await q.Skip(15 * page).Take(15).ToArrayAsyncLinqToDB();
    }

    public async Task<Quote?> GetQuoteByKeywordAsync(ulong guildId, string keyword)
    {
        await using var uow = _db.GetDbContext();
        var quotes = await uow.GetTable<Quote>()
                              .Where(q => q.GuildId == guildId && q.Keyword == keyword)
                              .ToArrayAsyncLinqToDB();

        return quotes.RandomOrDefault();
    }

    public async Task<IReadOnlyCollection<Quote>> SearchQuoteKeywordTextAsync(
        ulong guildId,
        string? keyword,
        string text)
    {
        keyword = keyword?.ToUpperInvariant();
        await using var uow = _db.GetDbContext();

        var quotes = await uow.GetTable<Quote>()
                              .Where(q => q.GuildId == guildId
                                          && (keyword == null || q.Keyword == keyword))
                              .ToArrayAsync();

        var toReturn = new List<Quote>(quotes.Length);

        foreach (var q in quotes)
        {
            if (q.AuthorName.Contains(text, StringComparison.InvariantCultureIgnoreCase)
                || q.Text.Contains(text, StringComparison.InvariantCultureIgnoreCase))
            {
                toReturn.Add(q);
            }
        }

        return toReturn;
    }

    public async Task<IReadOnlyCollection<Quote>> GetGuildQuotesAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var quotes = await uow.GetTable<Quote>()
                              .Where(x => x.GuildId == guildId)
                              .ToListAsyncLinqToDB();
        return quotes;
    }

    public Task<int> RemoveAllByKeyword(ulong guildId, string keyword)
    {
        keyword = keyword.ToUpperInvariant();

        using var uow = _db.GetDbContext();

        var count = uow.GetTable<Quote>()
                       .Where(x => x.GuildId == guildId && x.Keyword == keyword)
                       .DeleteAsync();

        return count;
    }

    public async Task<Quote?> GetQuoteByIdAsync(ulong guildId, int quoteId)
    {
        await using var uow = _db.GetDbContext();

        var quote = await uow.GetTable<Quote>()
                             .Where(x => x.Id == quoteId && x.GuildId == guildId)
                             .FirstOrDefaultAsyncLinqToDB();

        return quote;
    }

    public async Task<Quote> AddQuoteAsync(
        ulong guildId,
        ulong authorId,
        string authorName,
        string keyword,
        string text)
    {
        keyword = keyword.ToUpperInvariant();

        Quote q;
        await using var uow = _db.GetDbContext();
        uow.Set<Quote>()
           .Add(q = new()
           {
               AuthorId = authorId,
               AuthorName = authorName,
               GuildId = guildId,
               Keyword = keyword,
               Text = text
           });
        await uow.SaveChangesAsync();

        return q;
    }

    public async Task<Quote?> EditQuoteAsync(ulong authorId, int quoteId, string text)
    {
        await using var uow = _db.GetDbContext();
        var result = await uow.GetTable<Quote>()
                              .Where(x => x.Id == quoteId && x.AuthorId == authorId)
                              .Set(x => x.Text, text)
                              .UpdateWithOutputAsync((del, ins) => ins);

        var q = result.FirstOrDefault();
        return q;
    }

    public async Task<Quote?> EditQuoteAsync(
        ulong guildId,
        int quoteId,
        string keyword,
        string text)
    {
        await using var uow = _db.GetDbContext();
        var result = await uow.GetTable<Quote>()
                              .Where(x => x.Id == quoteId && x.GuildId == guildId)
                              .Set(x => x.Keyword, keyword)
                              .Set(x => x.Text, text)
                              .UpdateWithOutputAsync((del, ins) => ins);

        var q = result.FirstOrDefault();
        return q;
    }

    public async Task<bool> DeleteQuoteAsync(
        ulong guildId,
        ulong authorId,
        bool isQuoteManager,
        int quoteId)
    {
        await using var uow = _db.GetDbContext();
        var q = uow.Set<Quote>().GetById(quoteId);


        var count = await uow.GetTable<Quote>()
                             .Where(x => x.GuildId == guildId && x.Id == quoteId)
                             .Where(x => isQuoteManager || (x.AuthorId == authorId))
                             .DeleteAsync();


        return count > 0;
    }

    public async Task<bool> ImportQuotesAsync(ulong guildId, string input)
    {
        Dictionary<string?, List<ExportedQuote?>> data;
        try
        {
            data = Yaml.Deserializer.Deserialize<Dictionary<string?, List<ExportedQuote?>>>(input);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Quote import failed: {Message}", ex.Message);
            return false;
        }


        var toImport = data.SelectMany(x => x.Value.Select(v => (Key: x.Key, Value: v)))
                           .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value?.Txt));

        await using var uow = _db.GetDbContext();
        await uow.GetTable<Quote>()
                 .BulkCopyAsync(toImport
                     .Select(q => new Quote
                     {
                         GuildId = guildId,
                         Keyword = q.Key,
                         Text = q.Value.Txt,
                         AuthorId = q.Value.Aid,
                         AuthorName = q.Value.An
                     }));

        return true;
    }

    public async Task<(IReadOnlyCollection<Quote> quotes, int totalCount)> FindQuotesAsync(
        ulong guildId,
        string query,
        int page)
    {
        await using var uow = _db.GetDbContext();

        var baseQuery = uow.GetTable<Quote>()
                           .Where(x => x.GuildId == guildId)
                           .Where(x => x.Keyword.Contains(query) || x.Text.Contains(query));
        
        var quotes = await baseQuery
                           .OrderBy(x => x.Id)
                           .Skip((page - 1) * 10)
                           .Take(10)
                           .ToListAsyncLinqToDB();

        return (quotes, await baseQuery.CountAsyncLinqToDB());
    }
}