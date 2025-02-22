using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Common.TypeReaders;

public sealed class ShmartBankInputAmountReader : BaseShmartInputAmountReader
{
    private readonly IBankService _bank;

    public ShmartBankInputAmountReader(IBankService bank, DbService db, GamblingConfigService gambling)
        : base(db, gambling)
    {
        _bank = bank;
    }

    protected override Task<long> Cur(ICommandContext ctx)
        => _bank.GetBalanceAsync(ctx.User.Id);

    protected override Task<long> Max(ICommandContext ctx)
        => Cur(ctx);
}