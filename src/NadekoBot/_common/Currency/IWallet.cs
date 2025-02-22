namespace NadekoBot.Services.Currency;

public interface IWallet
{
    public ulong UserId { get; }

    public Task<long> GetBalance();
    public Task<bool> Take(long amount, TxData? txData);
    public Task Add(long amount, TxData? txData);

    public async Task<bool> Transfer(
        long amount,
        IWallet to,
        TxData? txData)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than 0.");

        if (txData is not null)
            txData = txData with
            {
                OtherId = to.UserId
            };
        
        var succ = await Take(amount, txData);

        if (!succ)
            return false;

        if (txData is not null)
            txData = txData with
            {
                OtherId = UserId
            };

        await to.Add(amount, txData);

        return true;
    }
}