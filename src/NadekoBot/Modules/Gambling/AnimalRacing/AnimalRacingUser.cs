using NadekoBot.Modules.Games.Common;

namespace NadekoBot.Modules.Gambling.Common.AnimalRacing;

public sealed class AnimalRacingUser(string username, ulong userId, long bet, RaceAnimal animal)
{
    public long Bet { get; } = bet;
    public string Username { get; } = username;
    public ulong UserId { get; } = userId;
    public RaceAnimal Animal { get; } = animal;
    public int Progress { get; set; }
}
