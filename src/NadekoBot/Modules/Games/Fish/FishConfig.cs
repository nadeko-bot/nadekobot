using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Games;

public sealed class FishConfig
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 2;

    public string WeatherSeed { get; set; } = string.Empty;
    public bool RequireCaptcha { get; set; } = true;
    public List<string> StarEmojis { get; set; } = new();
    public List<string> SpotEmojis { get; set; } = new();
    public FishChance Chance { get; set; } = new FishChance();

    [Comment("Min currency awarded when fishing out currency.")]
    public long CurrencyMin { get; set; } = 1;

    [Comment("Max currency awarded when fishing out currency.")]
    public long CurrencyMax { get; set; } = 1;
    
    public List<FishData> Fish { get; set; } = new();
    public List<FishData> Trash { get; set; } = new();
    public List<FishItem> Items {get;set;} = new();
}