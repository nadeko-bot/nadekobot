using NadekoBot.Hosted;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiAuthTests
{
    private const string KEY = "placeholder-key-xxxxxxxxxxxxxxx";

    [Test]
    public void ApiKey_MatchesOnlyTheExactBearerToken()
    {
        var key = new ApiKey(KEY);

        Assert.That(key.Matches($"Bearer {KEY}"), Is.True);
        Assert.That(key.Matches($"bearer  {KEY}"), Is.True);

        foreach (var header in new[]
                 {
                     "", KEY, "Bearer wrong-key", $"Bearer {KEY}extra", $"Basic {KEY}", "Bearer " + new string('x', 4096)
                 })
            Assert.That(key.Matches(header), Is.False, header.Length > 40 ? "long header" : header);

        Assert.That(new ApiKey(string.Empty).Matches("Bearer "), Is.False);
    }
}
