#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Discord;
using NadekoBot.Common;
using NadekoBot.Hosted;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class IntentTests
{
    private const ulong BOT_ID = 42;
    private const string PREFIX = ".";

    [Test]
    public void ApplicationFlags_GiveTheApprovedAndSwitchedOnIntents()
    {
        // Presence approved after a review, Server Members switched on below the limit, and an unrelated flag.
        var some = IntentState.FromApplicationFlags((1L << 12) | (1L << 15) | (1L << 23));
        Assert.That(some, Is.EqualTo(new IntentState(true, true, false)));
        Assert.That(some.GatewayIntents,
            Is.EqualTo(GatewayIntents.AllUnprivileged | GatewayIntents.GuildPresences | GatewayIntents.GuildMembers));
        Assert.That(some.MissingNames(), Is.EqualTo("Message Content"));

        Assert.That(IntentState.FromApplicationFlags((1L << 13) | (1L << 14) | (1L << 18)).GatewayIntents,
            Is.EqualTo(GatewayIntents.All));
        Assert.That(IntentState.FromApplicationFlags(0).GatewayIntents, Is.EqualTo(GatewayIntents.AllUnprivileged));

        using var messageContentOnly = JsonDocument.Parse("""{"id":"1","flags":786432}""");
        Assert.That(DiscordTokenChecker.ParseIntents(messageContentOnly.RootElement), Is.EqualTo(new IntentState(false, false, true)));
        using var noFlags = JsonDocument.Parse("""{"id":"1"}""");
        Assert.That(DiscordTokenChecker.ParseIntents(noFlags.RootElement), Is.Null);
        using var textFlags = JsonDocument.Parse("""{"id":"1","flags":"786432"}""");
        Assert.That(DiscordTokenChecker.ParseIntents(textFlags.RootElement), Is.Null);
    }

    [Test]
    public void MentionPrefix_RunsTheTextAfterTheBotMentionAsACommand()
    {
        var cases = new (string Content, string? Command)[]
        {
            ("<@42> help", ".help"),
            ("  <@!42>   .h ping", ".h ping"),
            ("<@42>help", ".help"),
            ("<@42>   ", null),
            ("<@43> help", null),
            ("hi <@42> help", null),
            ("<@42 help", null),
            (".help", null)
        };

        foreach (var (content, expected) in cases)
        {
            var ok = MentionPrefix.TryStrip(content, BOT_ID, PREFIX, out var command);
            Assert.That((ok, command), Is.EqualTo((expected is not null, expected)), content);
        }
    }

    [Test]
    public void IntentsFile_KeepsEachSetAndRefusesAnUnknownFile()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var intents = new IntentState((mask & 1) == 0, (mask & 2) == 0, (mask & 4) == 0);
            Assert.That(intents.MissingMask, Is.EqualTo(mask));
            Assert.That(IntentsFile.Parse(IntentsFile.Format(intents)), Is.EqualTo(intents));
        }

        Assert.That(IntentsFile.Parse("presence,guilds"), Is.Null);
        Assert.That(IntentsFile.Parse("presence\nmembers"), Is.Null);

        var path = Path.Combine(Path.GetTempPath(), "nadeko-intents-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.That(IntentsFile.Read(path), Is.Null);
            IntentsFile.Write(path, new IntentState(false, true, true));
            Assert.That(IntentsFile.Read(path), Is.EqualTo(new IntentState(false, true, true)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
