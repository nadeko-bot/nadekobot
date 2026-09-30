using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NadekoBot.Modules.Games.Common.Acrophobia;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class AcrophobiaTests
{
    [Test]
    public void GetWinners_ReturnsEveryTopVotedSubmission()
    {
        KeyValuePair<AcrophobiaUser, int> Vote(ulong id, int votes)
            => new(new(id, $"user{id}", "x"), votes);

        var tie = AcrophobiaGame.GetWinners([Vote(1, 2), Vote(2, 1), Vote(3, 2)]);
        var single = AcrophobiaGame.GetWinners([Vote(1, 0), Vote(2, 3)]);
        var none = AcrophobiaGame.GetWinners([Vote(1, 0), Vote(2, 0)]);

        Assert.That(tie.Select(x => x.Key.UserId), Is.EqualTo(new ulong[] { 1, 3 }));
        Assert.That(single.Select(x => x.Key.UserId), Is.EqualTo(new ulong[] { 2 }));
        Assert.That(none, Is.Empty);
    }

    [Test]
    public async Task Submission_AcceptsOneMatchingAnswerPerUser()
    {
        using var game = new AcrophobiaGame(new());
        var answer = string.Join(' ', game.StartingLetters.Select(static c => c + "ord"));
        var tooShort = string.Join(' ', game.StartingLetters.Skip(1).Select(static c => c + "ord"));

        Assert.That(await game.UserInput(1, "a", answer), Is.True);
        Assert.That(await game.UserInput(1, "a", answer), Is.False);
        Assert.That(await game.UserInput(2, "b", tooShort), Is.False);
        Assert.That(await game.UserInput(2, "b", answer.ToLowerInvariant()), Is.True);
    }
}
