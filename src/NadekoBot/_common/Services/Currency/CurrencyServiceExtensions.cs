using NadekoBot.Services.Currency;

namespace NadekoBot.Services;

public static class CurrencyServiceExtensions
{
    public static async Task<long> GetBalanceAsync(this ICurrencyService cs, ulong userId)
    {
        var wallet = await cs.GetWalletAsync(userId);
        return await wallet.GetBalance();
    }

    // FUTURE should be a transaction
    public static async Task<bool> TransferAsync(
        this ICurrencyService cs,
        IMessageSenderService sender,
        IUser from,
        IUser to,
        long amount,
        string? note,
        string formattedAmount)
    {
        var fromWallet = await cs.GetWalletAsync(from.Id);
        var toWallet = await cs.GetWalletAsync(to.Id);

        var extra = new TxData("gift", from.ToString()!, note, from.Id);

        if (await fromWallet.Transfer(amount, toWallet, extra))
        {
            try
            {
                await sender.Response(to)
                            .Confirm(string.IsNullOrWhiteSpace(note)
                                ? $"Received {formattedAmount} from {from} "
                                : $"Received {formattedAmount} from {from}: {note}")
                            .SendAsync();
            }
            catch
            {
                //ignored
            }

            return true;
        }

        return false;
    }
}