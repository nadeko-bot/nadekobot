using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility;

public interface IQuoteService
{
    /// <summary>
    /// Delete all quotes created by the author in a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <param name="userId">ID of the user</param>
    /// <returns>Number of deleted qutoes</returns>
    Task<int> DeleteAllAuthorQuotesAsync(ulong guildId, ulong userId);

    /// <summary>
    /// Delete all quotes in a guild
    /// </summary>
    /// <param name="guildId">ID of the guild</param>
    /// <returns>Number of deleted qutoes</returns>
    Task<int> DeleteAllQuotesAsync(ulong guildId);

    Task<IReadOnlyCollection<Quote>> GetAllQuotesAsync(ulong guildId, int page, OrderType order);
    Task<Quote?> GetQuoteByKeywordAsync(ulong guildId, string keyword);

    Task<IReadOnlyCollection<Quote>> SearchQuoteKeywordTextAsync(
        ulong guildId,
        string? keyword,
        string text);
    
    Task<(IReadOnlyCollection<Quote> quotes, int totalCount)> FindQuotesAsync(ulong guildId, string query, int page);

    Task<IReadOnlyCollection<Quote>> GetGuildQuotesAsync(ulong guildId);
    Task<int> RemoveAllByKeyword(ulong guildId, string keyword);
    Task<Quote?> GetQuoteByIdAsync(ulong guildId, int quoteId);

    Task<Quote> AddQuoteAsync(
        ulong guildId,
        ulong authorId,
        string authorName,
        string keyword,
        string text);

    Task<Quote?> EditQuoteAsync(ulong authorId, int quoteId, string text);
    Task<Quote?> EditQuoteAsync(ulong guildId, int quoteId, string keyword, string text);

    Task<bool> DeleteQuoteAsync(
        ulong guildId,
        ulong authorId,
        bool isQuoteManager,
        int quoteId);

    Task<bool> ImportQuotesAsync(ulong guildId, string input);
}