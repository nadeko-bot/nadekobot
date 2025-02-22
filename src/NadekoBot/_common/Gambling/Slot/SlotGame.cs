namespace NadekoBot.Modules.Gambling;

//here is a payout chart
//https://lh6.googleusercontent.com/-i1hjAJy_kN4/UswKxmhrbPI/AAAAAAAAB1U/82wq_4ZZc-Y/DE6B0895-6FC1-48BE-AC4F-14D1B91AB75B.jpg
//thanks to judge for helping me with this
public class SlotGame
{
    private static readonly NadekoRandom _rng = new NadekoRandom();

    public SlotResult Spin(decimal bet)
    {
        var rolls = new[]
        {
            (byte)_rng.Next(0, 7),
            (byte)_rng.Next(0, 7),
            (byte)_rng.Next(0, 7)
        };

        ref var a = ref rolls[0];
        ref var b = ref rolls[1];
        ref var c = ref rolls[2];
        
        var multi = 0;
        var winType = SlotWinType.None;
        if (a == b && b == c)
        {
            if (a == 6)
            {
                winType = SlotWinType.TrippleJoker;
                multi = 25;
            }
            else
            {
                winType = SlotWinType.TrippleNormal;
                multi = 15;
            }
        }
        else if (a == 6 && (b == 6 || c == 6)
                 || (b == 6 && c == 6))
        {
            winType = SlotWinType.DoubleJoker;
            multi = 6;
        }
        else if (a == 6 || b == 6 || c == 6)
        {
            winType = SlotWinType.SingleJoker;
            multi = 1;
        }

        return new()
        {
            Won = bet * multi,
            WinType = winType,
            Multiplier = multi,
            Rolls = rolls,
        };
    }
}

public enum SlotWinType : byte
{
    None,
    SingleJoker,
    DoubleJoker,
    TrippleNormal,
    TrippleJoker,
}

/*
var rolls = new[]
        {
            _rng.Next(default(byte), 6),
            _rng.Next(default(byte), 6),
            _rng.Next(default(byte), 6)
        };
        
        var multi = 0;
        var winType = SlotWinType.None;

        ref var a = ref rolls[0];
        ref var b = ref rolls[1];
        ref var c = ref rolls[2];
        if (a == b && b == c)
        {
            if (a == 5)
            {
                winType = SlotWinType.TrippleJoker;
                multi = 30;
            }
            else
            {
                winType = SlotWinType.TrippleNormal;
                multi = 10;
            }
        }
        else if (a == 5 && (b == 5 || c == 5)
                 || (b == 5 && c == 5))
        {
            winType = SlotWinType.DoubleJoker;
            multi = 4;
        }
        else if (rolls.Any(x => x == 5))
        {
            winType = SlotWinType.SingleJoker;
            multi = 1;
        }

        return new()
        {
            Won = bet * multi,
            WinType = winType,
            Multiplier = multi,
            Rolls = rolls,
        };
    }
*/