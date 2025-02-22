#nullable disable
using NadekoBot.Common.Yml;
using Cloneable;

namespace NadekoBot.Common;

[Cloneable]
public partial class ImageUrls : ICloneable<ImageUrls> 
{
    [Comment("DO NOT CHANGE")]
    public int Version { get; set; } = 6;

    public CoinData Coins { get; set; }
    public Uri[] Currency { get; set; }
    public Uri[] Dice { get; set; }
    public XpData Xp { get; set; }

    public SlotData Slots { get; set; }

    public class SlotData
    {
        public Uri[] Emojis { get; set; }
        public Uri Bg { get; set; }
    }

    public class CoinData
    {
        public Uri[] Heads { get; set; }
        public Uri[] Tails { get; set; }
    }

    public class XpData
    {
        public Uri Bg { get; set; }
    }
}