using System;
using System.Threading.Tasks;
using Discord;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Tests.Waifu;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class ChannelLockTests
{
    private const ulong GUILD_ID = 111;
    private const ulong CHANNEL_ID = 222;

    private TestDbService _db = null!;
    private ChannelLockService _svc = null!;
    private ITextChannel _channel = null!;
    private OverwritePermissions? _overwrite;
    private bool _discordFails;

    [SetUp]
    public void Setup()
    {
        _db = new TestDbService();
        _svc = new ChannelLockService(_db);

        var everyone = Substitute.For<IRole>();
        var guild = Substitute.For<IGuild>();
        guild.EveryoneRole.Returns(everyone);

        _channel = Substitute.For<ITextChannel>();
        _channel.Id.Returns(CHANNEL_ID);
        _channel.GuildId.Returns(GUILD_ID);
        _channel.Guild.Returns(guild);
        _channel.GetPermissionOverwrite(everyone).Returns(_ => _overwrite);
        _channel.AddPermissionOverwriteAsync(everyone, Arg.Any<OverwritePermissions>(), Arg.Any<RequestOptions>())
                .Returns(ci =>
                {
                    if (_discordFails)
                        throw new InvalidOperationException("403");

                    _overwrite = ci.ArgAt<OverwritePermissions>(1);
                    return Task.CompletedTask;
                });
        _channel.RemovePermissionOverwriteAsync(everyone, Arg.Any<RequestOptions>())
                .Returns(_ =>
                {
                    _overwrite = null;
                    return Task.CompletedTask;
                });
    }

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    private async Task<int> CountRowsAsync()
    {
        await using var ctx = _db.GetDbContext();
        return await ctx.GetTable<LockedChannel>().CountAsyncLinqToDB();
    }

    [Test]
    public async Task LockThenUnlock_RestoresTheOriginalOverwrite_AndSavesOnlyAfterDiscordAccepts()
    {
        _discordFails = true;
        Assert.ThrowsAsync<InvalidOperationException>(() => _svc.LockAsync(_channel));
        Assert.That(await CountRowsAsync(), Is.Zero);
        _discordFails = false;

        _overwrite = new OverwritePermissions(sendMessages: PermValue.Allow,
            attachFiles: PermValue.Deny,
            createPrivateThreads: PermValue.Deny);
        var original = _overwrite.Value;

        Assert.That(await _svc.LockAsync(_channel), Is.EqualTo(ChannelLockResult.Success));
        Assert.That(ChannelLockService.IsLocked(_channel), Is.True);
        Assert.That(_overwrite!.Value.SendMessages, Is.EqualTo(PermValue.Deny));
        Assert.That(_overwrite.Value.CreatePublicThreads, Is.EqualTo(PermValue.Deny));
        Assert.That(_overwrite.Value.AttachFiles, Is.EqualTo(PermValue.Deny));

        Assert.That(await _svc.LockAsync(_channel), Is.EqualTo(ChannelLockResult.AlreadyLocked));

        // a moderator gives Send Messages back by hand, then locks again: the unlock still restores the original
        _overwrite = _overwrite.Value.Modify(sendMessages: PermValue.Inherit);
        Assert.That(await _svc.LockAsync(_channel), Is.EqualTo(ChannelLockResult.Success));

        Assert.That(await _svc.UnlockAsync(_channel), Is.EqualTo(ChannelLockResult.Success));
        Assert.That(_overwrite!.Value.AllowValue, Is.EqualTo(original.AllowValue));
        Assert.That(_overwrite.Value.DenyValue, Is.EqualTo(original.DenyValue));
        Assert.That(await CountRowsAsync(), Is.Zero);

        Assert.That(await _svc.UnlockAsync(_channel), Is.EqualTo(ChannelLockResult.NotLocked));
        Assert.That(_overwrite!.Value.DenyValue, Is.EqualTo(original.DenyValue));

        _overwrite = null;
        await _svc.LockAsync(_channel);
        Assert.That(_overwrite, Is.Not.Null);

        await _svc.UnlockAsync(_channel);
        Assert.That(_overwrite, Is.Null);
    }
}
