namespace NadekoBot.Modules.Searches;

public interface IStockDataService
{
    public Task<StockData?> GetStockDataAsync(string symbol);
    Task<IReadOnlyCollection<SymbolData>> SearchSymbolAsync(string query);
    Task<IReadOnlyCollection<CandleData>> GetCandleDataAsync(string query);
}