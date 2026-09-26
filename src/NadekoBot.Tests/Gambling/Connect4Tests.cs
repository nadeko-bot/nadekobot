using System;
using System.Threading.Tasks;
using NadekoBot.Modules.Gambling.Common.Connect4;
using NUnit.Framework;

namespace NadekoBot.Tests.Gambling;

public class Connect4Tests
{
    private const ulong P1 = 1;
    private const ulong P2 = 2;

    [Test]
    public async Task TurnTimeout_EndsGameForTheIdlePlayer_AndInvalidColumnIsRejected()
    {
        using var game = new Connect4Game(P1, "p1", new Connect4Game.Options { TurnTimer = 1 });

        var ended = new TaskCompletionSource<Connect4Game.Result>();
        game.OnGameEnded += (_, result) =>
        {
            ended.TrySetResult(result);
            return Task.CompletedTask;
        };

        Assert.That(await game.Join(P2, "p2"), Is.True);

        var mover = game.CurrentPlayer.UserId;
        Assert.That(await game.Input(mover, Connect4Game.NUMBER_OF_COLUMNS + 1), Is.False);

        var finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(finished, Is.SameAs(ended.Task));
        Assert.That(ended.Task.Result, Is.EqualTo(Connect4Game.Result.OtherPlayerWon));
        Assert.That(await game.Input(mover, 1), Is.False);
        Assert.That(game.CurrentPhase, Is.EqualTo(Connect4Game.Phase.Ended));
    }
}
