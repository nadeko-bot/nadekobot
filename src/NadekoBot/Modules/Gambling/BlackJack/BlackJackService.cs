#nullable disable
using NadekoBot.Modules.Gambling.Common.Blackjack;

namespace NadekoBot.Modules.Gambling.Services;

public class BlackJackService : INService
{
    public ConcurrentDictionary<ulong, Blackjack> Games { get; } = new();
}