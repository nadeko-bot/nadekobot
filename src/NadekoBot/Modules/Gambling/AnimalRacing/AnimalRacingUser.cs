#nullable disable
using NadekoBot.Modules.Games.Common;

namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

public class AnimalRacingUser
{
    public long Bet { get; }
    public string Username { get; }
    public ulong UserId { get; }
    public RaceAnimal Animal { get; set; }
    public int Progress { get; set; }

    public AnimalRacingUser(string username, ulong userId, long bet)
    {
        Bet = bet;
        Username = username;
        UserId = userId;
    }

    public override bool Equals(object obj)
        => obj is AnimalRacingUser x ? x.UserId == UserId : false;

    public override int GetHashCode()
        => UserId.GetHashCode();
}