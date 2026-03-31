using NadekoBot.Common.Yml;
using SixLabors.ImageSharp.PixelFormats;
using YamlDotNet.Serialization;
using Color = SixLabors.ImageSharp.Color;

namespace NadekoBot.Modules.Gambling.Common;

public sealed class GamblingConfig
{
    [Comment("""DO NOT CHANGE""")]
    public int Version { get; set; } = 13;

    [Comment("""Currency settings""")]
    public CurrencyConfig Currency { get; set; }

    [Comment("""Minimum amount users can bet (>=0)""")]
    public int MinBet { get; set; } = 0;

    [Comment("""
             Maximum amount users can bet
             Set 0 for unlimited
             """)]
    public int MaxBet { get; set; } = 0;

    [Comment("""Settings for betflip command""")]
    public BetFlipConfig BetFlip { get; set; }

    [Comment("""Settings for betroll command""")]
    public BetRollConfig BetRoll { get; set; }

    [Comment("""Automatic currency generation settings.""")]
    public GenerationConfig Generation { get; set; }

    [Comment("""
             Settings for timely command 
             (letting people claim X amount of currency every Y hours)
             """)]
    public TimelyConfig Timely { get; set; }

    [Comment("""How much will each user's owned currency decay over time.""")]
    public DecayConfig Decay { get; set; }

    [Comment("""What is the bot's cut on some transactions""")]
    public BotCutConfig BotCuts { get; set; }

    [Comment("""Settings for LuckyLadder command""")]
    public LuckyLadderSettings LuckyLadder { get; set; }

    [Comment("""
             Amount of currency selfhosters will get PER pledged dollar CENT.
             1 = 100 currency per $. Used almost exclusively on public nadeko.
             """)]
    public decimal PatreonCurrencyPerCent { get; set; } = 1;

    [Comment("""
             Currency reward per vote.
             This will work only if you've set up VotesApi and correct credentials for topgg and/or discords voting
             """)]
    public long VoteReward { get; set; } = 100;

    [Comment("""
             Id of the channel to send a message to after a user votes
             """)]
    public ulong? VoteFeedChannelId { get; set; }

    [Comment("""
                      List of platforms for which the bot will give currency rewards.
                      Format: PLATFORM|URL
                      Supported platforms: topgg, discords, discordbotlist
                      You will have to have VotesApi running on the same machine.
                      Format example: Top.gg|https://top.gg/bot/YOUR_BOT_ID/vote
             """)]
    public string[] VotePlatforms { get; set; } = [];

    [Comment("""Slot config""")]
    public SlotsConfig Slots { get; set; }

    [Comment("""
             Bonus config for server boosts
             """)]
    public BoostBonusConfig BoostBonus { get; set; }

    public GamblingConfig()
    {
        BetRoll = new();
        Currency = new();
        BetFlip = new();
        Generation = new();
        Timely = new();
        Decay = new();
        Slots = new();
        LuckyLadder = new();
        BotCuts = new();
        BoostBonus = new();
    }
}

public class CurrencyConfig
{
    [Comment("""What is the emoji/character which represents the currency""")]
    public string Sign { get; set; } = "🌸";

    [Comment("""What is the name of the currency""")]
    public string Name { get; set; } = "Nadeko Flower";

    [Comment("""
             For how long (in days) will the transactions be kept in the database (curtrs)
             Set 0 to disable cleanup (keep transactions forever)
             """)]
    public int TransactionsLifetime { get; set; } = 0;
}

public sealed class TimelyConfig
{
    [Comment("""
             How much currency will the users get every time they run .timely command
             setting to 0 or less will disable this feature
             """)]
    public long Amount { get; set; } = 0;

    [Comment("""
             How often (in hours) can users claim currency with .timely command
             setting to 0 or less will disable this feature
             """)]
    public int Cooldown { get; set; } = 24;

    [Comment("""
             How will timely be protected?
             None, Button (users have to click the button) or Captcha (users have to type the captcha from an image)
             """)]
    public TimelyProt ProtType { get; set; } = TimelyProt.Button;
}

public enum TimelyProt
{
    None,
    Button,
    Captcha
}

public sealed class BetFlipConfig
{
    [Comment("""Bet multiplier if user guesses correctly""")]
    public decimal Multiplier { get; set; } = 1.95M;
}

public sealed class BetRollConfig
{
    [Comment("""
             When betroll is played, user will roll a number 0-100.
             This setting will describe which multiplier is used for when the roll is higher than the given number.
             Doesn't have to be ordered.
             """)]
    public BetRollPair[] Pairs { get; set; } = Array.Empty<BetRollPair>();

    public BetRollConfig()
        => Pairs =
        [
            new()
            {
                WhenAbove = 99,
                MultiplyBy = 10
            },
            new()
            {
                WhenAbove = 90,
                MultiplyBy = 4
            },
            new()
            {
                WhenAbove = 65,
                MultiplyBy = 2
            }
        ];
}

public sealed class GenerationConfig
{
    [Comment("""
             when currency is generated, should it also have a random password
             associated with it which users have to type after the .pick command
             in order to get it
             """)]
    public bool HasPassword { get; set; } = true;

    [Comment("""
             Every message sent has a certain % chance to generate the currency
             specify the percentage here (1 being 100%, 0 being 0% - for example
             default is 0.02, which is 2%
             """)]
    public decimal Chance { get; set; } = 0.02M;

    [Comment("""How many seconds have to pass for the next message to have a chance to spawn currency""")]
    public int GenCooldown { get; set; } = 10;

    [Comment("""Minimum amount of currency that can spawn""")]
    public int MinAmount { get; set; } = 1;

    [Comment("""
             Maximum amount of currency that can spawn.
              Set to the same value as MinAmount to always spawn the same amount
             """)]
    public int MaxAmount { get; set; } = 1;
}

public sealed class DecayConfig
{
    [Comment("""
             Percentage of user's current currency which will be deducted every 24h. 
             0 - 1 (1 is 100%, 0.5 50%, 0 disabled)
             """)]
    public decimal Percent { get; set; } = 0;

    [Comment("""Maximum amount of user's currency that can decay at each interval. 0 for unlimited.""")]
    public int MaxDecay { get; set; } = 0;

    [Comment("""Only users who have more than this amount will have their currency decay.""")]
    public int MinThreshold { get; set; } = 99;

    [Comment("""How often, in hours, does the decay run. Default is 24 hours""")]
    public int HourInterval { get; set; } = 24;
}

public sealed class LuckyLadderSettings
{
    [Comment("""Self-Explanatory. Has to have 8 values, otherwise the command won't work.""")]
    public decimal[] Multipliers { get; set; }

    public LuckyLadderSettings()
        => Multipliers = [2.4M, 1.7M, 1.5M, 1.1M, 0.5M, 0.3M, 0.2M, 0.1M];
}

public sealed class SlotsConfig
{
    [Comment("""Hex value of the color which the numbers on the slot image will have.""")]
    public Rgba32 CurrencyFontColor { get; set; } = Color.Red;
}

public sealed class BetRollPair
{
    public int WhenAbove { get; set; }
    public float MultiplyBy { get; set; }
}

public sealed class BotCutConfig
{
    [Comment("""
             Shop sale cut percentage.
             Whenever a user buys something from the shop, bot will take a cut equal to this percentage.
             The rest goes to the user who posted the item/role/whatever to the shop.
             This is a good way to reduce the amount of currency in circulation therefore keeping the inflation in check.
             Default 0.1 (10%).
             """)]
    public decimal ShopSaleCut { get; set; } = 0.1m;
}

public sealed class BoostBonusConfig
{
    [Comment("Users will receive a bonus if they boost any of these servers")]
    public List<ulong> GuildIds { get; set; } = new();

    [Comment("This bonus will be added before any other multiplier is applied to the .timely command")]

    public long BaseTimelyBonus { get; set; } = 50;
}