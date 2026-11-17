#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Common;
using NadekoBot.Modules.Utility.AiAgent;
using NSubstitute;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class AiAgentActivityTests
{
    private const ulong GUILD_ID = 1;

    [Test]
    public async Task ActivityAndLimits_DescribeStepsAndDelayABusyMemberOrServer()
    {
        var strings = CreateStrings();

        string? Tool(string name, string args, bool failed = false)
        {
            using var doc = JsonDocument.Parse(args);
            return AgentActivityFormatter.FormatToolCall(name, doc.RootElement, failed, ".", strings, GUILD_ID);
        }

        var run = Tool("run_command", """{"command":"mute <@123> 10m spam"}""");
        Assert.That(run, Does.Contain("`.mute`").And.Not.Contain("123").And.Not.Contain("spam"));
        Assert.That(Tool("run_command", """{"command":".ban <@5>"}"""), Does.Contain("`.ban`").And.Not.Contain(".."));

        Assert.That(Tool("ask_user", """{"question":"which one?"}"""), Is.Null);
        Assert.That(Tool("close_session", "{}"), Is.Null);

        Assert.That(Tool("invoke_data_tool", """{"name":"get_user_xp"}"""), Does.Contain("user xp"));
        Assert.That(Tool("send_message", """{"channel":"789","message":"hi"}"""), Does.Contain("<#789>"));
        Assert.That(Tool("some_new_tool", "{}"), Does.Contain("agent_step_other").And.Contain("some new tool"));
        Assert.That(
            Tool("web_search", """{"query":"weather"}""", failed: true),
            Does.StartWith("🌐 agent_step_failed(agent_step_web_search(weather))"));

        var note = AgentActivityFormatter.FormatNote("First line\n\nsecond   line " + new string('x', 300));
        Assert.That(note, Does.StartWith("💭 First line second line").And.EndWith("...").And.Not.Contain("\n"));
        Assert.That(note.Length, Is.LessThan(220));

        var lines = Enumerable.Range(0, 64).Select(static i => $"line{i:00} " + new string('x', 140)).ToList();

        var text = AgentActivityFormatter.Render(lines, 10, 2000, "**Working**", true, strings, GUILD_ID);

        Assert.That(text.Length, Is.LessThanOrEqualTo(2000));
        Assert.That(text, Does.StartWith("**Working**\n-# *agent_activity_earlier("));
        Assert.That(text, Does.Contain("line63").And.Not.Contain("line00"));

        var shown = text.Split('\n').Count(static l => l.StartsWith("-# line", StringComparison.Ordinal));
        Assert.That(text, Does.Contain($"agent_activity_earlier({10 + 64 - shown})"));

        var all = AgentActivityFormatter.Render(lines.Take(3).ToList(), 0, 4096, null, false, strings, GUILD_ID);
        Assert.That(all.Split('\n'), Has.Length.EqualTo(3));
        Assert.That(all, Does.Not.Contain("agent_activity_earlier"));

        // A busy member or server waits for a free place, and one wait notice covers a crowd.
        var time = new FakeTimeProvider();
        var limiter = new AgentLimiter(time);
        var notices = 0;

        // A wait which never ends fails the test instead of a hang.
        static async Task<T> SoonAsync<T>(Task<T> task, [CallerArgumentExpression(nameof(task))] string wait = "")
        {
            try
            {
                return await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"This wait hung: {wait}");
            }
        }
        Task NoticeAsync()
        {
            notices++;
            return Task.CompletedTask;
        }

        for (var i = 0; i < AgentLimiter.MEMBER_BURST; i++)
            Assert.That(await SoonAsync(limiter.WaitForMemberAsync(1, NoticeAsync, CancellationToken.None)), Is.True);

        var member = limiter.WaitForMemberAsync(1, NoticeAsync, CancellationToken.None);
        var again = limiter.WaitForMemberAsync(1, NoticeAsync, CancellationToken.None);
        Assert.That(member.IsCompleted || again.IsCompleted, Is.False);
        Assert.That(notices, Is.EqualTo(1));
        time.Advance(AgentLimiter.MemberRefill);
        Assert.That(new[] { await SoonAsync(member), await SoonAsync(again) }, Is.EquivalentTo(new[] { true, false }));

        var slots = new List<GuildSlot?>();
        for (var i = 0; i < AgentLimiter.GUILD_CONCURRENT_REQUESTS; i++)
            slots.Add(await SoonAsync(limiter.EnterGuildAsync(2, NoticeAsync, CancellationToken.None)));
        Assert.That(slots, Is.All.Not.Null);

        var server = limiter.EnterGuildAsync(2, NoticeAsync, CancellationToken.None);
        Assert.That(server.IsCompleted, Is.False);
        slots[0]!.Dispose();
        slots[0]!.Dispose();
        Assert.That(await SoonAsync(server), Is.Not.Null);
        Assert.That(notices, Is.EqualTo(2));

        // The second Dispose freed nothing, so the next requests wait, and one more than the waiting cap is busy at once.
        using var stopWaiting = new CancellationTokenSource();
        var waiting = new List<Task<GuildSlot?>>();
        for (var i = 0; i < AgentLimiter.GUILD_MAX_WAITING; i++)
            waiting.Add(limiter.EnterGuildAsync(2, NoticeAsync, stopWaiting.Token));
        Assert.That(waiting.Exists(static w => w.IsCompleted), Is.False);
        Assert.That(await SoonAsync(limiter.EnterGuildAsync(2, NoticeAsync, CancellationToken.None)), Is.Null);
        await stopWaiting.CancelAsync();

        // A request which never ran gives its token back.
        limiter.RefundMember(1);
        Assert.That(await SoonAsync(limiter.WaitForMemberAsync(1, NoticeAsync, CancellationToken.None)), Is.True);

        // Both budgets are empty after 5 edits. After one shard refill, server 3 still has no token and gives the shard token to server 4.
        for (var i = 0; i < AgentLimiter.GUILD_STATUS_BURST; i++)
            Assert.That(limiter.TryTakeStatusEdit(3), Is.True);
        Assert.That(limiter.TryTakeStatusEdit(3), Is.False);
        time.Advance(AgentLimiter.ShardStatusRefill);
        Assert.That(limiter.TryTakeStatusEdit(3), Is.False);
        Assert.That(limiter.TryTakeStatusEdit(4), Is.True);

        // One "busy" error in each interval for each server.
        Assert.That(limiter.TryNotifyBusy(2), Is.True);
        Assert.That(limiter.TryNotifyBusy(2), Is.False);
        time.Advance(AgentLimiter.GuildNoticeInterval);
        Assert.That(limiter.TryNotifyBusy(2), Is.True);
    }

    private static IBotStrings CreateStrings()
    {
        var strings = Substitute.For<IBotStrings>();
        strings.GetText(Arg.Any<string>(), Arg.Any<ulong?>(), Arg.Any<object[]>())
            .Returns(static ci =>
            {
                var key = ci.ArgAt<string>(0);
                var data = ci.ArgAt<object[]>(2);
                return data.Length == 0 ? key : $"{key}({string.Join("|", data)})";
            });
        return strings;
    }
}
