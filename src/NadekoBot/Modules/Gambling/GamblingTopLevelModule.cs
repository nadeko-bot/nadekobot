#nullable disable
using NadekoBot.Modules.Gambling.Services;
using System.Numerics;

namespace NadekoBot.Modules.Gambling.Common;

public abstract class GamblingModule<TService> : NadekoModule<TService>
{
    protected GamblingConfig Config
        => _lazyConfig.Value;

    protected string CurrencySign
        => Config.Currency.Sign;

    protected string CurrencyName
        => Config.Currency.Name;

    private readonly Lazy<GamblingConfig> _lazyConfig;

    protected GamblingModule(GamblingConfigService gambService)
        => _lazyConfig = new(() => gambService.Data);

    private async Task<bool> InternalCheckBet(long amount)
    {
        if (amount < 1)
            return false;
        
        if (amount < Config.MinBet)
        {
            await Response().Error(strs.min_bet_limit(Format.Bold(Config.MinBet.ToString()) + CurrencySign)).SendAsync();
            return false;
        }

        if (Config.MaxBet > 0 && amount > Config.MaxBet)
        {
            await Response().Error(strs.max_bet_limit(Format.Bold(Config.MaxBet.ToString()) + CurrencySign)).SendAsync();
            return false;
        }

        return true;
    }

    protected string N<T>(T cur)
        where T : INumber<T>
        => CurrencyHelper.N(cur, Culture, CurrencySign);

    protected Task<bool> CheckBetMandatory(long amount)
    {
        if (amount < 1)
            return Task.FromResult(false);
        return InternalCheckBet(amount);
    }

    protected Task<bool> CheckBetOptional(long amount)
    {
        if (amount == 0)
            return Task.FromResult(true);
        return InternalCheckBet(amount);
    }
}