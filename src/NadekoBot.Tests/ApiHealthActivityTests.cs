#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Db.Models;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiHealthActivityTests
{
    private const ulong GUILD = 1;
    private const ulong OTHER_GUILD = 2;
    private const int BOT_HIERARCHY = 10;

    private static readonly DateTimeOffset _start = new(2026, 10, 14, 10, 30, 0, TimeSpan.Zero);

    private TestDbService _db = null!;
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new TestDbService();
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-activity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void Health_ReportsEachIssueMergesEqualChecksAndListsProblemsFirst()
    {
        var guild = new FakeGuild(
            new GuildPermissions(viewChannel: true, sendMessages: true, manageRoles: true, kickMembers: true),
            topRoleId: 50,
            roles: new()
            {
                [1] = new HealthRole(5, false, false),
                [2] = new HealthRole(BOT_HIERARCHY + 2, false, false),
                [3] = new HealthRole(1, true, false)
            },
            channels: new()
            {
                [100] = new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true),
                [101] = new ChannelPermissions(viewChannel: true, sendMessages: true),
                [102] = new ChannelPermissions(sendMessages: true)
            });

        var sources = HealthSources.Empty with
        {
            Greets =
            [
                new(GreetType.Greet, true, 103, "hi"),
                new(GreetType.Bye, true, 101, "bye %user%"),
                new(GreetType.Boost, true, 101, """{"embeds":[{"description":"thanks"}]}"""),
                new(GreetType.GreetDm, true, null, "hi")
            ],
            XpRewardRoles = [1, 1, 2],
            NotifyChannels = [null, 102],
            Punishments =
            [
                new(PunishmentAction.Kick, null),
                new(PunishmentAction.Ban, null),
                new(PunishmentAction.AddRole, null),
                new(PunishmentAction.AddRole, 3)
            ],
            AutoAssignRoles = [4],
            MuteRoleId = 4,
            ReactionRoles = [new(102, 1)],
            ButtonRoles = [new(102, 1)],
            LogChannels = [100]
        };

        var dto = GuildHealth.Evaluate(guild, sources, new HealthTimersDto(0, 0, 0, null));
        var checks = dto.Checks.Select(static c => (c.Feature, c.Status, c.Issue, c.Permission, c.ChannelId, c.RoleId, c.Count))
            .ToArray();

        Assert.That(checks, Is.EquivalentTo(new (string, string, string?, string?, string?, string?, int)[]
        {
            ("greet", "broken", "channel_missing", null, "103", null, 1),
            ("bye", "warning", "channel_no_access", "EmbedLinks", "101", null, 1),
            ("boost", "broken", "channel_no_access", "EmbedLinks", "101", null, 1),
            ("greetDm", "ok", null, null, null, null, 1),
            ("xpRewards", "ok", null, null, null, "1", 2),
            ("xpRewards", "broken", "role_above_bot", null, null, "2", 1),
            ("levelUpNotify", "ok", null, null, null, null, 1),
            ("levelUpNotify", "broken", "channel_no_access", "ViewChannel", "102", null, 1),
            ("warnPunishments", "ok", null, null, null, null, 1),
            ("warnPunishments", "broken", "missing_permission", "BanMembers", null, null, 1),
            ("warnPunishments", "broken", "role_missing", null, null, null, 1),
            ("warnPunishments", "broken", "role_managed", null, null, "3", 1),
            ("autoAssign", "broken", "role_missing", null, null, "4", 1),
            ("muteRole", "warning", "role_missing", null, null, "4", 1),
            ("reactionRoles", "broken", "channel_no_access", "ViewChannel", "102", "1", 1),
            ("buttonRoles", "ok", null, null, "102", "1", 1),
            ("log", "ok", null, null, "100", null, 1),
            ("selfAssign", "off", null, null, null, null, 0),
            ("streamNotify", "off", null, null, null, null, 0),
            ("repeaters", "off", null, null, null, null, 0)
        }));

        var order = new[] { "broken", "warning", "off", "ok" };
        var ranks = dto.Checks.Select(c => Array.IndexOf(order, c.Status)).ToArray();
        Assert.That(ranks, Is.Ordered);

        Assert.That(dto.Bot.RoleId, Is.EqualTo("50"));
        Assert.That(dto.Bot.RolePosition, Is.EqualTo(BOT_HIERARCHY));
        Assert.That(dto.Bot.Administrator, Is.False);
        Assert.That(dto.Bot.Missing, Is.EqualTo(new[]
        {
            "EmbedLinks", "AttachFiles", "ReadMessageHistory", "AddReactions", "ManageMessages", "ManageChannels",
            "BanMembers", "ModerateMembers", "MuteMembers"
        }));

        var admin = GuildHealth.Evaluate(new FakeGuild(new GuildPermissions(administrator: true), null, new(), new()),
            HealthSources.Empty,
            new HealthTimersDto(0, 0, 0, null));
        Assert.That(admin.Bot.Missing, Is.Empty);
        Assert.That(admin.Bot.RoleId, Is.Null);
        Assert.That(admin.Checks.Select(static c => c.Status), Is.All.EqualTo("off"));
    }

    [Test]
    public void Health_CapsTheChecksAndKeepsEachFeature()
    {
        var repeaters = Enumerable.Range(0, GuildHealth.MAX_CHECKS + 100)
            .Select(static i => new RepeaterSource((ulong)(1000 + i), "x"))
            .ToArray();
        var guild = new FakeGuild(new GuildPermissions(administrator: true),
            null,
            new(),
            new() { [100] = new ChannelPermissions(viewChannel: true, sendMessages: true, embedLinks: true) });
        var sources = HealthSources.Empty with { Repeaters = repeaters, LogChannels = [100] };

        var dto = GuildHealth.Evaluate(guild, sources, new HealthTimersDto(0, 0, 0, null));

        Assert.That(dto.Checks, Has.Count.EqualTo(GuildHealth.MAX_CHECKS));
        Assert.That(dto.Checks.Select(static c => c.Feature).Distinct().Count(), Is.EqualTo(Enum.GetValues<HealthFeature>().Length));
        Assert.That(dto.Checks.Single(static c => c.Feature == "log").Status, Is.EqualTo("ok"));
    }

    [Test]
    public async Task HealthSources_ReadOnlyTheGuildAndCountOnlyFutureTimers()
    {
        var now = _start.UtcDateTime;
        await using (var ctx = _db.GetDbContext())
        {
            ctx.AddRange(
                new GreetSettings { GuildId = GUILD, GreetType = GreetType.Bye, IsEnabled = true, ChannelId = 5, MessageText = "bye" },
                new GreetSettings { GuildId = GUILD, GreetType = GreetType.Greet, IsEnabled = false, ChannelId = 6 },
                new GreetSettings { GuildId = OTHER_GUILD, GreetType = GreetType.Greet, IsEnabled = true, ChannelId = 7 },
                new Notify { GuildId = GUILD, Type = NotifyType.LevelUp, ChannelId = null },
                new Notify { GuildId = GUILD, Type = NotifyType.Protection, ChannelId = 9 },
                new WarningPunishment { GuildId = GUILD, Count = 3, Punishment = PunishmentAction.AddRole, RoleId = 11 },
                new GuildConfig { GuildId = GUILD, AutoAssignRoleIds = "12, 13", MuteRoleId = 14 },
                new Repeater { GuildId = GUILD, ChannelId = 15, Message = "hey", Interval = "01:00:00" },
                new XpSettings { GuildId = GUILD, RoleRewards = [new XpRoleReward { Level = 5, RoleId = 16 }] },
                new XpSettings { GuildId = OTHER_GUILD, RoleRewards = [new XpRoleReward { Level = 5, RoleId = 17 }] },
                new UnmuteTimer { GuildId = GUILD, UserId = 1, UnmuteAt = now.AddHours(2) },
                new UnmuteTimer { GuildId = GUILD, UserId = 2, UnmuteAt = now.AddHours(-1) },
                new UnbanTimer { GuildId = GUILD, UserId = 3, UnbanAt = now.AddHours(1) },
                new UnbanTimer { GuildId = OTHER_GUILD, UserId = 4, UnbanAt = now.AddMinutes(1) },
                new TempRole { GuildId = GUILD, UserId = 5, RoleId = 18, ExpiresAt = now.AddDays(1) });
            await ctx.SaveChangesAsync();
        }

        var src = await HealthEndpoints.ReadSourcesAsync(_db, GUILD);
        Assert.That(src.Greets, Is.EqualTo(new[] { new GreetSource(GreetType.Bye, true, 5, "bye") }));
        Assert.That(src.NotifyChannels, Is.EqualTo(new ulong?[] { null }));
        Assert.That(src.Punishments, Is.EqualTo(new[] { new PunishSource(PunishmentAction.AddRole, 11) }));
        Assert.That(src.AutoAssignRoles, Is.EqualTo(new ulong[] { 12, 13 }));
        Assert.That(src.MuteRoleId, Is.EqualTo(14));
        Assert.That(src.Repeaters, Is.EqualTo(new[] { new RepeaterSource(15, "hey") }));
        Assert.That(src.XpRewardRoles, Is.EqualTo(new ulong[] { 16 }));

        var timers = await HealthEndpoints.ReadTimersAsync(_db, GUILD, now);
        Assert.That((timers.Mutes, timers.Bans, timers.TempRoles), Is.EqualTo((1, 1, 1)));
        Assert.That(timers.NextExpiry, Is.EqualTo(now.AddHours(1)));
        Assert.That(timers.NextExpiry!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task Activity_KeepsCountsAcrossARestartByDayAndHour()
    {
        var time = new FakeTimeProvider(_start);
        var svc = new GuildActivityService(DbPath, time);
        for (var i = 0; i < 3; i++)
            svc.Record(ActivityKind.Command, GUILD, "xp");
        svc.Record(ActivityKind.Error, GUILD);
        svc.Record(ActivityKind.Join, GUILD);
        svc.Record(ActivityKind.Leave, GUILD);
        svc.Record(ActivityKind.Command, OTHER_GUILD, "xp");

        var before = await ReadAsync(svc, GuildActivityService.RETENTION_DAYS);
        await svc.StopAsync();

        svc = new GuildActivityService(DbPath, time);
        var after = await ReadAsync(svc, ActivityEndpoints.DEFAULT_DAYS);

        foreach (var dto in new[] { before, after })
        {
            var today = dto.Days[^1];
            Assert.That(today.Date, Is.EqualTo(new DateOnly(2026, 10, 14)));
            Assert.That((today.Commands, today.Errors, today.Joins, today.Leaves), Is.EqualTo((3L, 1L, 1L, 1L)));
            Assert.That(dto.Since, Is.EqualTo(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(dto.Hours, Has.Count.EqualTo(24));
            Assert.That(dto.Hours[10], Is.EqualTo(3));
            Assert.That(dto.Hours.Sum(), Is.EqualTo(3));
            Assert.That(dto.TopCommands, Is.EqualTo(new[] { new TopCommandDto("xp", 3) }));
        }

        Assert.That(before.Days, Has.Count.EqualTo(GuildActivityService.RETENTION_DAYS));
        Assert.That(after.Days, Has.Count.EqualTo(ActivityEndpoints.DEFAULT_DAYS));
        Assert.That(after.Days[0].Date, Is.EqualTo(new DateOnly(2026, 10, 8)));

        time.Advance(TimeSpan.FromDays(1));
        svc.Record(ActivityKind.Command, GUILD, "xp");
        var nextDay = await ReadAsync(svc, ActivityEndpoints.DEFAULT_DAYS);
        Assert.That(nextDay.Days.Select(static d => d.Commands).TakeLast(2), Is.EqualTo(new[] { 3L, 1L }));
        Assert.That(nextDay.TopCommands, Is.EqualTo(new[] { new TopCommandDto("xp", 4) }));
        await svc.StopAsync();

        svc = new GuildActivityService(DbPath, time);
        var empty = await ReadAsync(svc, ActivityEndpoints.DEFAULT_DAYS, 99);
        await svc.StopAsync();
        Assert.That(await svc.ReadAsync(GUILD, ActivityEndpoints.DEFAULT_DAYS, CancellationToken.None), Is.Null);
        Assert.That(empty.Since, Is.Null);
        Assert.That(empty.Days.Select(static d => d.Commands), Is.All.Zero);
        Assert.That(empty.TopCommands, Is.Empty);
    }

    [Test]
    public async Task Activity_KeepsTheMostUsedCommandsUnderTheCapAndRanksTiesByName()
    {
        var time = new FakeTimeProvider(_start);
        var svc = new GuildActivityService(DbPath, time);
        var runs = new Dictionary<string, int> { ["zz"] = 5, ["aa"] = 5, ["mm"] = 4, ["kk"] = 3, ["jj"] = 3 };
        for (var i = 0; i < ActivityStore.MAX_COMMANDS_PER_DAY + 10; i++)
            runs[$"n{i:00}"] = 1;

        foreach (var (name, count) in runs)
        {
            for (var i = 0; i < count; i++)
                svc.Record(ActivityKind.Command, GUILD, name);
        }

        await svc.StopAsync();
        svc = new GuildActivityService(DbPath, time);
        var dto = await ReadAsync(svc, ActivityEndpoints.DEFAULT_DAYS);
        await svc.StopAsync();

        Assert.That(dto.Days[^1].Commands, Is.EqualTo(runs.Values.Sum()));
        Assert.That(dto.TopCommands, Is.EqualTo(new[]
        {
            new TopCommandDto("aa", 5), new TopCommandDto("zz", 5), new TopCommandDto("mm", 4),
            new TopCommandDto("jj", 3), new TopCommandDto("kk", 3)
        }));
        Assert.That(Scalar("SELECT COUNT(*) FROM command_runs"), Is.EqualTo(ActivityStore.MAX_COMMANDS_PER_DAY));
    }

    [Test]
    public async Task Activity_DeletesOldDaysAndReplacesABrokenFile()
    {
        await File.WriteAllTextAsync(DbPath, "not a database");
        var time = new FakeTimeProvider(_start);
        var svc = new GuildActivityService(DbPath, time);
        Assert.That(File.Exists(DbPath + ActivityStore.BROKEN_SUFFIX), Is.True);

        svc.Record(ActivityKind.Command, GUILD, "xp");
        await svc.StopAsync();

        time.Advance(TimeSpan.FromDays(GuildActivityService.RETENTION_DAYS));
        svc = new GuildActivityService(DbPath, time);
        var beforeWrite = await ReadAsync(svc, GuildActivityService.RETENTION_DAYS);
        svc.Record(ActivityKind.Join, GUILD);
        await svc.StopAsync();

        svc = new GuildActivityService(DbPath, time);
        var dto = await ReadAsync(svc, GuildActivityService.RETENTION_DAYS);
        await svc.StopAsync();

        Assert.That(beforeWrite.Since, Is.Null);
        Assert.That(beforeWrite.TopCommands, Is.Empty);
        Assert.That(dto.Since, Is.EqualTo(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(dto.Days.Sum(static d => d.Commands), Is.Zero);
        Assert.That(dto.Days[^1].Joins, Is.EqualTo(1));
        Assert.That(Scalar("SELECT COUNT(*) FROM hourly"), Is.EqualTo(1));
        Assert.That(Scalar("SELECT COUNT(*) FROM command_runs"), Is.Zero);

        Assert.That(new[] { null, "7", "30", "14", "+7", " 7", "" }.Select(ActivityEndpoints.ParseDays),
            Is.EqualTo(new int?[] { 7, 7, 30, null, null, null, null }));
    }

    [Test]
    public async Task Activity_CountsWarningsForEachUtcDayOfTheServer()
    {
        var firstDay = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.GetDbContext())
        {
            ctx.AddRange(
                new Warning { GuildId = GUILD, UserId = 1, Reason = "a", Moderator = "m", DateAdded = firstDay.AddMinutes(-1) },
                new Warning { GuildId = GUILD, UserId = 1, Reason = "b", Moderator = "m", DateAdded = firstDay },
                new Warning { GuildId = GUILD, UserId = 2, Reason = "c", Moderator = "m", DateAdded = firstDay.AddHours(23.9) },
                new Warning { GuildId = GUILD, UserId = 2, Reason = "d", Moderator = "m", DateAdded = firstDay.AddDays(6).AddHours(12) },
                new Warning { GuildId = GUILD, UserId = 2, Reason = "e", Moderator = "m", DateAdded = firstDay.AddDays(7) },
                new Warning { GuildId = OTHER_GUILD, UserId = 3, Reason = "f", Moderator = "m", DateAdded = firstDay.AddDays(2) });
            await ctx.SaveChangesAsync();
        }

        var unixDay = (long)(firstDay - DateTime.UnixEpoch).TotalDays;
        var counts = await ActivityEndpoints.CountWarningsPerDayAsync(_db, GUILD, unixDay, ActivityEndpoints.DEFAULT_DAYS);

        Assert.That(counts, Is.EqualTo(new long[] { 2, 0, 0, 0, 0, 0, 1 }));
    }

    private string DbPath
        => Path.Combine(_dir, "activity.db");

    private static async Task<GuildActivityDto> ReadAsync(GuildActivityService svc, int days, ulong guildId = GUILD)
    {
        var report = await svc.ReadAsync(guildId, days, CancellationToken.None);
        Assert.That(report, Is.Not.Null);
        return ActivityEndpoints.ToDto(report!, new long[days]);
    }

    private long Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    private readonly struct FakeGuild(
        GuildPermissions perms,
        ulong? topRoleId,
        Dictionary<ulong, HealthRole> roles,
        Dictionary<ulong, ChannelPermissions> channels) : IHealthGuild
    {
        public GuildPermissions BotPermissions
            => perms;

        public int BotHierarchy
            => BOT_HIERARCHY;

        public ulong? BotTopRoleId
            => topRoleId;

        public HealthRole? GetRole(ulong roleId)
            => roles.TryGetValue(roleId, out var role) ? role : null;

        public ChannelPermissions? GetChannelPermissions(ulong channelId)
            => channels.TryGetValue(channelId, out var p) ? p : null;
    }
}
