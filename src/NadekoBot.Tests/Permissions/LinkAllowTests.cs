#nullable enable
using System;
using System.Collections.Frozen;
using System.Threading.Tasks;
using NadekoBot.Modules.Permissions.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Permissions;

public sealed class LinkAllowTests
{
    private TestDbService _db = null!;

    [SetUp]
    public void SetUp()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void EveryLinkMustGoToAnAllowedDomain()
    {
        var allowed = new[] { "youtube.com", "tenor.com" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        (string Text, bool Blocked)[] cases =
        [
            ("no links here, just youtube.com as text", false),
            ("https://www.youtube.com/watch?v=1", false),
            ("look HTTPS://M.YOUTUBE.COM/x and <https://tenor.com/view/1>", false),
            ("[clip](https://youtube.com:443/x)", false),
            ("hi https://evil.com", true),
            ("https://youtube.com/x https://evil.com/y", true),
            ("https://notyoutube.com", true),
            ("https://youtube.com.evil.com", true),
            ("https://youtube.com@evil.com/", true),
            ("https://evil.com\\@youtube.com", true),
            ("https:///evil.com", true),
            ("https://[::1]/", true),
            ("ftp://files.evil.com", true),
            ("steam://run/440", false),
            ("https://evil.com./x", true),
            ("https://youtube.com./x", false),
            ("https://evil.com\t@youtube.com", true),
            ("https://youtube.com%2e.evil.com", true),
        ];

        foreach (var (text, blocked) in cases)
            Assert.That(LinkHosts.HasBlockedLink(text, allowed), Is.EqualTo(blocked), text);

        Assert.That(LinkHosts.HasBlockedLink("https://youtube.com", null), Is.True);

        var idn = new[] { "xn--bcher-kva.de" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(LinkHosts.HasBlockedLink("https://www.bücher.de/x", idn), Is.False);
    }

    [Test]
    public void NormalizeDomain_KeepsOnlyTheHost()
    {
        Assert.That(LinkHosts.NormalizeDomain(" https://www.YouTube.com/watch?v=1 "), Is.EqualTo("www.youtube.com"));
        Assert.That(LinkHosts.NormalizeDomain("*.tenor.com"), Is.EqualTo("tenor.com"));
        Assert.That(LinkHosts.NormalizeDomain("bücher.de"), Is.EqualTo("xn--bcher-kva.de"));

        foreach (var bad in new[] { "", "localhost", "-bad.com", "a..com", "https://", "exa mple.com" })
            Assert.That(LinkHosts.NormalizeDomain(bad), Is.Null, bad);
    }

    [Test]
    public async Task Toggle_AddsRemovesAndStopsAtTheLimit()
    {
        const ulong guildId = 5;

        Assert.That(await FilterService.ToggleAllowedLinkDomainAsync(_db, guildId, "youtube.com"),
            Is.EqualTo(LinkAllowResult.Added));
        Assert.That(await FilterService.ToggleAllowedLinkDomainAsync(_db, guildId, "youtube.com"),
            Is.EqualTo(LinkAllowResult.Removed));

        for (var i = 0; i < FilterService.MAX_ALLOWED_DOMAINS; i++)
        {
            Assert.That(await FilterService.ToggleAllowedLinkDomainAsync(_db, guildId, $"d{i}.com"),
                Is.EqualTo(LinkAllowResult.Added));
        }

        Assert.That(await FilterService.ToggleAllowedLinkDomainAsync(_db, guildId, "one-more.com"),
            Is.EqualTo(LinkAllowResult.LimitReached));
        Assert.That(await FilterService.ToggleAllowedLinkDomainAsync(_db, guildId + 1, "one-more.com"),
            Is.EqualTo(LinkAllowResult.Added));
        Assert.That(await FilterService.ToggleAllowedLinkDomainAsync(_db, guildId, "d0.com"),
            Is.EqualTo(LinkAllowResult.Removed));
    }
}
