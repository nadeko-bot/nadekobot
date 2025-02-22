namespace NadekoBot.Modules.Searches;

public interface IStockChartDrawingService
{
    Task<ImageData?> GenerateSparklineAsync(IReadOnlyCollection<CandleData> series);
    Task<ImageData?> GenerateCombinedChartAsync(IReadOnlyCollection<CandleData> series);
    Task<ImageData?> GenerateCandleChartAsync(IReadOnlyCollection<CandleData> series);
}