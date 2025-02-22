using Cloneable;
using NadekoBot.Common.Yml;

namespace NadekoBot.Modules.Games;

[Cloneable]
public sealed partial class FishConfig : ICloneable<FishConfig>
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 1;

    public string WeatherSeed { get; set; } = string.Empty;
    public List<string> StarEmojis { get; set; } = new();
    public List<string> SpotEmojis { get; set; } = new();
    public FishChance Chance { get; set; } = new FishChance();
    // public List<FishBait> Baits { get; set; } = new();
    // public List<FishingPole> Poles { get; set; } = new();
    public List<FishData> Fish { get; set; } = new();
    public List<FishData> Trash { get; set; } = new();
}

// public sealed class FishBait : ICloneable<FishBait>
// {
//     public int Id { get; set; }
//     public string Name { get; set; } = string.Empty;
//     public long Price { get; set; }
//     public string Emoji { get; set; } = string.Empty;
//     public int StackSize { get; set; } = 100;
//
//     public string? OnlyWeather { get; set; }
//     public string? OnlySpot { get; set; }
//     public string? OnlyTime { get; set; }
//
//     public double FishMulti { get; set; } = 1;
//     public double TrashMulti { get; set; } = 1;
//     public double NothingMulti { get; set; } = 1;
//
//     public double RareFishMulti { get; set; } = 1;
//     public double RareTrashMulti { get; set; } = 1;
//     
//     public double MaxStarMulti { get; set; } = 1;
// }
//
// public sealed class FishingPole : ICloneable<FishingPole>
// {
//     public int Id { get; set; }
//     public string Name { get; set; } = string.Empty;
//     public long Price { get; set; }
//     public string Emoji { get; set; } = string.Empty;
//     public string Img { get; set; } = string.Empty;
//
//     public double FishMulti { get; set; } = 1;
//     public double TrashMulti { get; set; } = 1;
//     public double NothingMulti { get; set; } = 1;
// }