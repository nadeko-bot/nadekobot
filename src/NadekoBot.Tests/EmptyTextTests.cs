#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Db.Models;
using NadekoBot.Extensions;
using NadekoBot.Modules.NadekoExpressions;
using NadekoBot.Modules.Games.Common.Trivia;
using NadekoBot.Modules.Utility.Starboard;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class EmptyTextTests
{
    private const ulong GUILD = 1;

    [Test]
    public void Trivia_DoesNotAcceptAnEmptyAnswer()
    {
        var stopWord = new TriviaQuestion(new TriviaQuestionModel { Answer = "The" });
        Assert.That(stopWord.IsAnswerCorrect(""), Is.False);
        Assert.That(stopWord.IsAnswerCorrect("!!"), Is.False);
        Assert.That(stopWord.IsAnswerCorrect("the"), Is.True);
        Assert.That(stopWord.IsAnswerCorrect(" THE "), Is.True);

        var normal = new TriviaQuestion(new TriviaQuestionModel { Answer = "Paris" });
        Assert.That(normal.IsAnswerCorrect(""), Is.False);
        Assert.That(normal.IsAnswerCorrect("paris"), Is.True);
    }

    [Test]
    public async Task ExpressionImport_SkipsAnEmptyTrigger()
    {
        using var db = new TestDbService();
        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        var svc = new NadekoExpressionsService(db,
            Substitute.For<IBotStrings>(),
            Substitute.For<IBot>(),
            client,
            Substitute.For<IPubSub>(),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<IReplacementService>(),
            Substitute.For<IPermissionChecker>(),
            null!,
            new ShardData(client, creds));

        const string yaml = """
                            "":
                              - res: everyone
                            "  ":
                              - res: everyone
                            hi:
                              - res: hello
                            """;

        Assert.That(await svc.ImportExpressionsAsync(GUILD, yaml), Is.True);

        await using var ctx = db.GetDbContext();
        var triggers = await ctx.GetTable<NadekoExpression>()
                                .Where(x => x.GuildId == GUILD)
                                .Select(x => x.Trigger)
                                .ToArrayAsyncLinqToDB();
        Assert.That(triggers, Is.EqualTo(new[] { "hi" }));
    }

    [Test]
    public void Starboard_NewCountKeepsTheOldPost()
    {
        var old = new EmbedBuilder()
                  .WithDescription("the starred text")
                  .WithFooter(StarboardService.FormatFooter("⭐", 3, 123456))
                  .Build();

        var updated = StarboardService.WithStarCount(old, "⭐", 7, 123456);

        Assert.That(updated.Description, Is.EqualTo("the starred text"));
        Assert.That(StarboardService.StarCountOf(updated), Is.EqualTo(7));
        Assert.That(StarboardService.TryGetEntryId(updated), Is.EqualTo(123456UL));
    }
}
