using NadekoBot.AiAgent;
using NadekoBot.Modules.Gambling.Services;
using NadekoBot.Modules.Utility.AiAgent;
using NadekoBot.Services;

namespace NadekoBot.Modules.Gambling;

public sealed class CurrencyAiAdapter(
    ICurrencyService currency,
    GamblingConfigService gamblingConfig) : IAiToolGroup, INService
{
    public string GroupName => "currency";
    public string GroupDescription => "Server currency: user balances, leaderboards, currency configuration.";

    [AiTool("get_user_balance", "Returns the currency balance for a user.")]
    public async Task<UserBalanceDto> GetUserBalance(
        AiToolContext ctx,
        [AiParam("Discord user ID")] ulong userId)
    {
        var balance = await currency.GetBalanceAsync(userId);
        var cfg = gamblingConfig.Data.Currency;
        return new(userId, balance, cfg.Sign, cfg.Name);
    }

    [AiTool("get_currency_leaderboard", "Returns the top users by currency balance in this server's currency.")]
    public async Task<CurrencyLeaderboardDto> GetCurrencyLeaderboard(
        AiToolContext ctx,
        [AiParam("How many users to return, max 25")] int top = 10)
    {
        top = Math.Clamp(top, 1, 25);

        var collected = new List<CurrencyLeaderboardEntryDto>(top);
        var page = 0;
        const int perPage = 9;

        while (collected.Count < top)
        {
            var batch = await currency.GetTopRichest(0UL, page, perPage);
            if (batch.Count == 0)
                break;

            foreach (var entry in batch)
            {
                if (collected.Count >= top)
                    break;

                collected.Add(new(entry.UserId, entry.Username, entry.CurrencyAmount, collected.Count + 1));
            }

            if (batch.Count < perPage)
                break;

            page++;
        }

        var cfg = gamblingConfig.Data.Currency;
        return new(cfg.Sign, cfg.Name, collected);
    }

    [AiTool("get_currency_settings", "Returns the server's currency configuration: sign, name, and transaction retention.")]
    public Task<CurrencySettingsDto> GetCurrencySettings(AiToolContext ctx)
    {
        var cfg = gamblingConfig.Data.Currency;
        return Task.FromResult(new CurrencySettingsDto(cfg.Sign, cfg.Name, cfg.TransactionsLifetime));
    }
}

public readonly record struct UserBalanceDto(ulong UserId, long Balance, string CurrencySign, string CurrencyName);

public sealed record CurrencyLeaderboardDto(string CurrencySign, string CurrencyName, List<CurrencyLeaderboardEntryDto> Entries);

public readonly record struct CurrencyLeaderboardEntryDto(ulong UserId, string? Username, long Balance, int Rank);

public readonly record struct CurrencySettingsDto(string Sign, string Name, int TransactionsLifetimeDays);
