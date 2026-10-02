using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Common;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class TempVoiceTests
{
    private const ulong GUILD_ID = 111;
    private const ulong OTHER_GUILD_ID = 222;

    private TestDbService _db = null!;
    private TempVoiceService _svc = null!;
    private FakeTimeProvider _time = null!;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();

        var client = Substitute.For<DiscordSocketClient>();
        var creds = Substitute.For<IBotCreds>();
        creds.TotalShards.Returns(1);

        _time = new FakeTimeProvider();
        _svc = new TempVoiceService(_db, client, new ShardData(client, creds), Substitute.For<IBotStrings>(), _time);
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public async Task ToggleHub_AddsRemovesAndStopsAtLimit()
    {
        for (var i = 1; i <= TempVoiceService.MAX_HUBS; i++)
            Assert.That(await _svc.ToggleHubAsync(GUILD_ID, (ulong)i), Is.EqualTo(TempVoiceHubResult.Added));

        Assert.That(await _svc.ToggleHubAsync(GUILD_ID, 100), Is.EqualTo(TempVoiceHubResult.LimitReached));
        Assert.That(await _svc.ToggleHubAsync(OTHER_GUILD_ID, 200), Is.EqualTo(TempVoiceHubResult.Added));

        Assert.That(await _svc.ToggleHubAsync(GUILD_ID, 1), Is.EqualTo(TempVoiceHubResult.Removed));

        var hubs = await _svc.GetHubsAsync(GUILD_ID);
        Assert.That(hubs, Has.Count.EqualTo(TempVoiceService.MAX_HUBS - 1));
        Assert.That(hubs.Contains(1UL), Is.False);
        Assert.That(await _svc.GetHubsAsync(OTHER_GUILD_ID), Is.EqualTo(new ulong[] { 200 }));
    }

    [Test]
    public void BuildOverwrites_KeepsHubPermsAndGivesOwnerControl()
    {
        const ulong ownerId = 5;
        const ulong roleId = 7;
        var denyConnect = new OverwritePermissions(connect: PermValue.Deny);

        var hub = new[]
        {
            new Overwrite(roleId, PermissionTarget.Role, denyConnect),
            new Overwrite(ownerId, PermissionTarget.User, denyConnect)
        };

        var result = TempVoiceService.BuildOverwrites(hub, ownerId);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.Single(x => x.TargetId == roleId).Permissions.Connect, Is.EqualTo(PermValue.Deny));

        var owner = result.Single(x => x.TargetId == ownerId).Permissions;
        Assert.That(owner.Connect, Is.EqualTo(PermValue.Allow));
        Assert.That(owner.ManageChannel, Is.EqualTo(PermValue.Allow));
        Assert.That(owner.MoveMembers, Is.EqualTo(PermValue.Allow));
    }

    [Test]
    public void Cooldown_BlocksNewChannelPerMemberFor30Seconds()
    {
        Assert.That(_svc.TryStartCooldown(1), Is.True);
        Assert.That(_svc.TryStartCooldown(1), Is.False);
        Assert.That(_svc.TryStartCooldown(2), Is.True);

        _time.Advance(TempVoiceService.CreateCooldown - TimeSpan.FromSeconds(1));
        Assert.That(_svc.TryStartCooldown(1), Is.False);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(_svc.TryStartCooldown(1), Is.True);
    }

    [Test]
    public void EmptyChannel_IsDeletedOnlyAfterDelay()
    {
        var emptySince = _time.GetTimestamp();

        _time.Advance(TempVoiceService.DeleteDelay - TimeSpan.FromSeconds(1));
        Assert.That(TempVoiceService.IsDueForDelete(emptySince, _time.GetTimestamp(), _time), Is.False);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(TempVoiceService.IsDueForDelete(emptySince, _time.GetTimestamp(), _time), Is.True);
    }

    [Test]
    public void ActionGap_DelaysRepeatedHubClicksFor3Seconds()
    {
        var last = _time.GetTimestamp();

        _time.Advance(TempVoiceService.ActionGap - TimeSpan.FromSeconds(1));
        Assert.That(TempVoiceService.RemainingActionDelay(last, _time.GetTimestamp(), _time),
            Is.EqualTo(TimeSpan.FromSeconds(1)));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(TempVoiceService.RemainingActionDelay(last, _time.GetTimestamp(), _time), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ReserveSlot_StopsAtServerLimitAndFreesOnRelease()
    {
        for (var i = 0; i < TempVoiceService.MAX_TEMP_CHANNELS_PER_GUILD; i++)
            Assert.That(_svc.TryReserveSlot(GUILD_ID), Is.True);

        Assert.That(_svc.TryReserveSlot(GUILD_ID), Is.False);
        Assert.That(_svc.TryReserveSlot(GUILD_ID), Is.False);
        Assert.That(_svc.TryReserveSlot(OTHER_GUILD_ID), Is.True);

        _svc.ReleaseSlot(GUILD_ID);
        Assert.That(_svc.TryReserveSlot(GUILD_ID), Is.True);
        Assert.That(_svc.TryReserveSlot(GUILD_ID), Is.False);
    }

    [Test]
    public void TrimName_KeepsLimitAndDoesNotSplitEmoji()
    {
        var name = new string('a', TempVoiceService.MAX_CHANNEL_NAME_LENGTH - 1) + "\U0001F600";

        var trimmed = TempVoiceService.TrimName(name);

        Assert.That(trimmed, Is.EqualTo(new string('a', TempVoiceService.MAX_CHANNEL_NAME_LENGTH - 1)));
        Assert.That(TempVoiceService.TrimName("short"), Is.EqualTo("short"));
    }
}
