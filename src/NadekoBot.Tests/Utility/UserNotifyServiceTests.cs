using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Extensions;
using NadekoBot.Modules.Utility.UserNotifications;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Utility;

public class UserNotifyServiceTests
{
    private const ulong USER_ID = 42;

    private TestDbService _db = null!;
    private UserNotifyService _svc = null!;

    private sealed class FakeRegistrar : IUserNotifyEventRegistrar
    {
        public IReadOnlyList<UserNotifyEventInfo> GetEvents()
            => [new("a", strs.user_notify_title), new("b", strs.user_notify_title), new("c", strs.user_notify_title)];
    }

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new UserNotifyService(_db,
            Substitute.For<DiscordSocketClient>(),
            Substitute.For<IMessageSenderService>(),
            Substitute.For<IBotCache>(),
            Substitute.For<IBotStrings>(),
            null!,
            [new FakeRegistrar()]);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task ToggleAndDisableAll_KeepOneRowPerTypeWithoutErrors()
    {
        var results = await Task.WhenAll(_svc.ToggleAsync(USER_ID, "a"), _svc.ToggleAsync(USER_ID, "a"));
        Assert.That(await _svc.IsBlockedAsync(USER_ID, "a"), Is.EqualTo(results.All(x => !x)));

        if (await _svc.IsBlockedAsync(USER_ID, "a"))
            Assert.That(await _svc.ToggleAsync(USER_ID, "a"), Is.True);

        Assert.That(await _svc.ToggleAsync(USER_ID, "a"), Is.False);
        Assert.That(await _svc.GetBlockedAsync(USER_ID), Is.EquivalentTo(new[] { "a" }));
        Assert.That(await _svc.ToggleAsync(USER_ID, "a"), Is.True);
        Assert.That(await _svc.IsBlockedAsync(USER_ID, "a"), Is.False);

        await _svc.ToggleAsync(USER_ID, "b");
        await _svc.DisableAllAsync(USER_ID);
        Assert.That((await _svc.GetBlockedAsync(USER_ID)).OrderBy(x => x), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(await _svc.IsBlockedAsync(USER_ID + 1, "a"), Is.False);
    }
}
