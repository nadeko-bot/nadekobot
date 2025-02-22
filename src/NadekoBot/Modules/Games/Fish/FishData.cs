namespace NadekoBot.Modules.Games;

public class FishData
{
    public required int Id { get; set; }
    public required string Name { get; set; }
    public FishingWeather? Weather { get; set; }
    public FishingSpot? Spot { get; set; }
    public FishingTime? Time { get; set; }
    public required double Chance { get; set; }
    public required int Stars { get; set; }
    public required string Fluff { get; set; }
    public List<string>? Condition { get; set; }
    public string? Image { get; init; }
    public string? Emoji { get; set; }
}