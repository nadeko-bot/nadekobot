#nullable disable
namespace NadekoBot.Modules.Searches;

public class StockData
{
    public string Name { get; set; }
    public string Symbol { get; set; }
    public double Price { get; set; }
    public string MarketCap { get; set; }
    public double Close { get; set; }
    public double Change50d { get; set; }
    public double Change200d { get; set; }
    public long DailyVolume { get; set; }
    public string Exchange { get; set; }
}