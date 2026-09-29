using System.Threading.Tasks;
using NadekoBot.Modules.Gambling.Common.AnimalRacing;
using NadekoBot.Modules.Gambling.Common.AnimalRacing.Exceptions;
using NadekoBot.Modules.Games.Common;
using NadekoBot.Services;
using NadekoBot.Services.Currency;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class AnimalRaceTests
{
    private const ulong USER_ID = 42;
    private const long BET = 100;

    [Test]
    public async Task JoinRace_Twice_ChargesTheBetOnlyOnce()
    {
        var currency = Substitute.For<ICurrencyService>();
        currency.RemoveAsync(Arg.Any<ulong>(), Arg.Any<long>(), Arg.Any<TxData?>()).Returns(true);

        RaceAnimal[] animals = [new() { Icon = "a" }, new() { Icon = "b" }, new() { Icon = "c" }];
        using var race = new AnimalRace(new RaceOptions(), currency, animals, null!);

        await race.JoinRace(USER_ID, "user", BET);

        Assert.ThrowsAsync<AlreadyJoinedException>(() => race.JoinRace(USER_ID, "user", BET));

        await currency.Received(1).RemoveAsync(USER_ID, BET, Arg.Any<TxData?>());
        Assert.That(race.Users.Count, Is.EqualTo(1));
    }
}
