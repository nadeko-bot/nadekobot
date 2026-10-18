using System;
using System.Net;
using System.Threading.Tasks;
using Discord.Net;
using NadekoBot.Db.Models;
using NadekoBot.Modules.Administration;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.Owner.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests.Administration;

public class TimedLimitsTests
{
    private static readonly DateTime _now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private TestDbService _db = null!;

    [SetUp]
    public void Setup()
        => _db = new TestDbService();

    [TearDown]
    public void TearDown()
        => _db.Dispose();

    [Test]
    public void TimedAction_WaitsAtMostADay_AndRetriesOnlyErrorsWhichCanGoAway()
    {
        Assert.That(TimedAction.GetWait(_now.AddDays(90), _now), Is.EqualTo(TimedAction.MaxWait));
        Assert.That(TimedAction.GetWait(_now.AddHours(3), _now), Is.EqualTo(TimeSpan.FromHours(3)));
        Assert.That(TimedAction.GetWait(_now.AddSeconds(-5), _now), Is.EqualTo(TimeSpan.Zero));

        Assert.That(TimedAction.ShouldRetry(new HttpException(HttpStatusCode.Forbidden, null!)), Is.False);
        Assert.That(TimedAction.ShouldRetry(new HttpException(HttpStatusCode.NotFound, null!)), Is.False);
        Assert.That(TimedAction.ShouldRetry(new HttpException(HttpStatusCode.BadRequest, null!)), Is.False);
        Assert.That(TimedAction.ShouldRetry(new InvalidOperationException()), Is.False);

        Assert.That(TimedAction.ShouldRetry(new HttpException(HttpStatusCode.ServiceUnavailable, null!)), Is.True);
        Assert.That(TimedAction.ShouldRetry(new HttpException(HttpStatusCode.TooManyRequests, null!)), Is.True);
        Assert.That(TimedAction.ShouldRetry(new TimeoutException()), Is.True);
    }

    [Test]
    public void AutoCommandInterval_IsFiveSecondsToThirtyDays()
    {
        var max = SelfService.MAX_AUTOCMD_INTERVAL_DAYS * 24 * 60 * 60;

        Assert.That(SelfService.IsValidAutoCommandInterval(SelfService.MIN_AUTOCMD_INTERVAL), Is.True);
        Assert.That(SelfService.IsValidAutoCommandInterval(max), Is.True);

        foreach (var bad in new[] { 0, SelfService.MIN_AUTOCMD_INTERVAL - 1, max + 1, int.MaxValue, -60 })
            Assert.That(SelfService.IsValidAutoCommandInterval(bad), Is.False, bad.ToString());
    }

    [Test]
    public async Task WarnPunish_LimitsOnlyTimeoutsToTwentyEightDays()
    {
        var svc = new UserPunishService(null!, _db, null!, null!, null!, null!, null!);
        var max = TimeSpan.FromDays(UserPunishService.MAX_TIMEOUT_DAYS);

        Assert.That(await svc.WarnPunish(1, 3, PunishmentAction.TimeOut, max), Is.True);
        Assert.That(await svc.WarnPunish(1, 3, PunishmentAction.TimeOut, max + TimeSpan.FromMinutes(1)), Is.False);
        Assert.That(await svc.WarnPunish(1, 4, PunishmentAction.Ban, TimeSpan.FromDays(59)), Is.True);
    }
}
