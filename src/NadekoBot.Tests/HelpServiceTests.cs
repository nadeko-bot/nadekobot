#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Nadeko.Common;
using NadekoBot.Common;
using NadekoBot.Common.Configs;
using NadekoBot.Extensions;
using NadekoBot.Modules.Help.Services;
using NadekoBot.Services;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class HelpServiceTests
{
    private BotConfigService _bcs = null!;
    private FakeTimeProvider _time = null!;
    private HelpService _svc = null!;

    [SetUp]
    public void Setup()
    {
        _bcs = new BotConfigService(new YamlSeria(), Substitute.For<IPubSub>(), new BotPolicy(false));
        ResetDmHelp();
        _time = new FakeTimeProvider();
        _svc = new HelpService(_bcs,
            Substitute.For<IReplacementService>(),
            Substitute.For<IMessageSenderService>(),
            _time);
    }

    [TearDown]
    public void TearDown()
        => ResetDmHelp();

    private void ResetDmHelp()
        => _bcs.ModifyConfig(static c =>
        {
            var defaults = new BotConfig();
            c.DmHelpText = defaults.DmHelpText;
            c.DmHelpTextKeywords = defaults.DmHelpTextKeywords;
        });

    [Test]
    public void Keywords_IgnoreCase_AndAnEmptyListMatchesEveryDm()
    {
        Assert.That(_svc.TryStartReply(1, "Help"), Is.True);
        Assert.That(_svc.TryStartReply(2, "show me COMMANDS"), Is.True);
        Assert.That(_svc.TryStartReply(3, "hi"), Is.False);

        _bcs.ModifyConfig(static c => c.DmHelpTextKeywords = []);
        Assert.That(_svc.TryStartReply(4, "hi"), Is.True);

        _bcs.ModifyConfig(static c => c.DmHelpTextKeywords = null!);
        Assert.That(_svc.TryStartReply(5, "hi"), Is.True);
    }

    [Test]
    public void Cooldown_AllowsOneReplyPerUserInTheWindow()
    {
        var granted = 0;
        Parallel.For(0, 64, _ =>
        {
            if (_svc.TryStartReply(1, "help"))
                Interlocked.Increment(ref granted);
        });
        Assert.That(granted, Is.EqualTo(1));

        Assert.That(_svc.TryStartReply(1, "help"), Is.False);
        Assert.That(_svc.TryStartReply(2, "help"), Is.True);

        _time.Advance(TimeSpan.FromSeconds(HelpService.DM_HELP_COOLDOWN_SECONDS));
        Assert.That(_svc.TryStartReply(1, "help"), Is.True);
    }

    [Test]
    public void NoDmHelpText_SendsNothing()
    {
        _bcs.ModifyConfig(static c => c.DmHelpText = "-");
        Assert.That(_svc.TryStartReply(1, "help"), Is.False);

        _bcs.ModifyConfig(static c => c.DmHelpText = "");
        Assert.That(_svc.TryStartReply(2, "help"), Is.False);
    }
}
