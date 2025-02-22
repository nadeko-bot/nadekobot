#nullable disable
using NadekoBot.Modules.Gambling.Bank;
using NadekoBot.Modules.Gambling.Services;

namespace NadekoBot.Common.TypeReaders;

public sealed class BalanceTypeReader : TypeReader
{
    private readonly BaseShmartInputAmountReader _tr;

    public BalanceTypeReader(DbService db, GamblingConfigService gambling)
    {
        _tr = new BaseShmartInputAmountReader(db, gambling); 
    }
    
    public override async Task<Discord.Commands.TypeReaderResult> ReadAsync(
        ICommandContext context,
        string input,
        IServiceProvider services)
    {

        var result = await _tr.ReadAsync(context, input);

        if (result.TryPickT0(out var val, out var err))
        {
            return Discord.Commands.TypeReaderResult.FromSuccess(val);
        }
        
        return Discord.Commands.TypeReaderResult.FromError(CommandError.Unsuccessful, err.Value);
    }
}

public sealed class BankBalanceTypeReader : TypeReader
{
    private readonly ShmartBankInputAmountReader _tr;

    public BankBalanceTypeReader(IBankService bank, DbService db, GamblingConfigService gambling)
    {
        _tr = new ShmartBankInputAmountReader(bank, db, gambling);
    }
    
    public override async Task<Discord.Commands.TypeReaderResult> ReadAsync(
        ICommandContext context,
        string input,
        IServiceProvider services)
    {

        var result = await _tr.ReadAsync(context, input);

        if (result.TryPickT0(out var val, out var err))
        {
            return Discord.Commands.TypeReaderResult.FromSuccess(val);
        }
        
        return Discord.Commands.TypeReaderResult.FromError(CommandError.Unsuccessful, err.Value);
    }
}