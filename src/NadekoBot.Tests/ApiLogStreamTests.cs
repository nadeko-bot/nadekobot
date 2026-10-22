#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Hosted;
using NUnit.Framework;
using Serilog;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiLogStreamTests
{
    private const string TOKEN = "MTA1.token-part.secret";
    private const string API_KEY = "placeholder-key-xxxxxxxxxxxxxxxx";

    [Test]
    public async Task Lines_WaitForTheSecretsAndNeverShowThem()
    {
        var time = new FakeTimeProvider();
        using var logs = new LogStreamService(time);
        using var logger = new LoggerConfiguration().WriteTo.Sink(logs.Sink).CreateLogger();
        using var sub = logs.Subscribe()!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        logger.Information("token {Token} key {Key}", TOKEN, API_KEY);
        await Task.Delay(100, cts.Token);
        Assert.That(sub.Reader.TryRead(out _), Is.False);

        logs.AddSecrets(TOKEN, API_KEY);
        var first = Parse(await sub.Reader.ReadAsync(cts.Token));
        Assert.That(first.Text, Is.EqualTo($"token {LogStreamService.REDACTED} key {LogStreamService.REDACTED}"));
        Assert.That(first.Level, Is.EqualTo("info"));

        logger.Error(new InvalidOperationException("boom " + TOKEN), "failed {Name}", "x");
        var error = Parse(await sub.Reader.ReadAsync(cts.Token));
        Assert.That(error.Level, Is.EqualTo("error"));
        Assert.That(error.Text, Does.StartWith("failed x\nSystem.InvalidOperationException: boom " + LogStreamService.REDACTED));
        Assert.That(error.Text, Does.Not.Contain(TOKEN));

        // The keepalive loop starts its timer on another thread, so advance until a tick lands.
        var read = sub.Reader.ReadAsync(cts.Token).AsTask();
        while (!read.IsCompleted)
        {
            time.Advance(LogStreamService.KeepAliveInterval);
            await Task.Delay(10, cts.Token);
        }

        Assert.That((await read).IsKeepAlive, Is.True);
    }

    [Test]
    public async Task Tail_GivesTheNewestLinesOldestFirst()
    {
        using var logs = new LogStreamService(TimeProvider.System);
        logs.AddSecrets(null, null);
        using var logger = new LoggerConfiguration().WriteTo.Sink(logs.Sink).CreateLogger();

        const int written = LogStreamService.BUFFER_CAPACITY + 10;
        const int batch = 500;
        // The sink queue drops lines above its limit, so the test writes in batches.
        for (var start = 0; start < written; start += batch)
        {
            for (var i = start; i < Math.Min(start + batch, written); i++)
                logger.Information("line {Index}", i);

            await WaitForLinesAsync(logs, Math.Min(start + batch, written));
        }

        var frames = new FeedFrame[LogStreamService.BUFFER_CAPACITY];
        var count = logs.Tail(int.MaxValue, frames, out var newest);
        Assert.That(count, Is.EqualTo(LogStreamService.BUFFER_CAPACITY));
        Assert.That(Parse(frames[0]).Text, Is.EqualTo("line 10"));
        Assert.That(Parse(frames[count - 1]).Text, Is.EqualTo($"line {written - 1}"));
        Assert.That(frames[count - 1].Id, Is.EqualTo(newest));

        Assert.That(logs.Tail(2, frames, out _), Is.EqualTo(2));
        Assert.That(Parse(frames[0]).Text, Is.EqualTo($"line {written - 2}"));
        Assert.That(logs.Tail(0, frames, out var newestForNone), Is.Zero);
        Assert.That(newestForNone, Is.EqualTo(newest));
    }

    [Test]
    public async Task Lines_SendTheTemplateAndEachValueAsTheTextShowsIt()
    {
        using var logs = new LogStreamService(TimeProvider.System);
        logs.AddSecrets(TOKEN, API_KEY);
        using var logger = new LoggerConfiguration().WriteTo.Sink(logs.Sink).CreateLogger();

        logger.Information("Shard {ShardId} ready in {Elapsed:F2}s", 0, 1.2345);
        logger.Information("Guild {GuildId} by {Name}", 1190000000000000099UL, "x");
        logger.Warning("login " + TOKEN + " {Key}", API_KEY);
        logger.ForContext<ApiLogStreamTests>().ForContext("LogSource", 3).Information("plain");

        var lines = await ReadLinesAsync(logs, 4);

        Assert.That(lines[0].Dto.Template, Is.EqualTo("Shard {ShardId} ready in {Elapsed:F2}s"));
        Assert.That(lines[0].Dto.Props, Is.EquivalentTo(new Dictionary<string, string> { ["ShardId"] = "0", ["Elapsed"] = "1.23" }));
        Assert.That(lines[0].Dto.Text, Is.EqualTo("Shard 0 ready in 1.23s"));
        Assert.That(lines[0].Raw, Does.Contain("\"ShardId\":\"0\""), "the keys keep the case of the property names");

        Assert.That(lines[1].Dto.Props!["GuildId"], Is.EqualTo("1190000000000000099"));
        Assert.That(lines[1].Dto.Props!["Name"], Is.EqualTo("x"));

        Assert.That(lines[2].Dto.Template, Is.EqualTo($"login {LogStreamService.REDACTED} {{Key}}"));
        Assert.That(lines[2].Dto.Props!["Key"], Is.EqualTo(LogStreamService.REDACTED));
        Assert.That(lines[2].Raw, Does.Not.Contain(TOKEN).And.Not.Contain(API_KEY));

        Assert.That(lines[3].Dto.Props!.Keys, Is.EquivalentTo(new[] { "SourceContext" }));
        Assert.That(lines[3].Dto.Props!["SourceContext"], Is.EqualTo(typeof(ApiLogStreamTests).FullName));

        // A field which is left out has no key, not a null.
        foreach (var line in lines)
            Assert.That(line.Raw, Does.Not.Contain("exceptionAt").And.Not.Contain("null"));
    }

    [Test]
    public async Task ExceptionAt_PointsAtTheNewlineAndIsLeftOutWhenTheCutRemovesIt()
    {
        using var logs = new LogStreamService(TimeProvider.System);
        logs.AddSecrets(TOKEN, API_KEY);
        using var logger = new LoggerConfiguration().WriteTo.Sink(logs.Sink).CreateLogger();

        logger.Error(new InvalidOperationException("boom"), "failed {Name}", "x");
        logger.Error(new InvalidOperationException("boom " + TOKEN), "failed {Name}\r\nagain", TOKEN);
        logger.Error(new InvalidOperationException("boom"), "{Long}", new string('a', LogStreamService.MAX_TEXT_LENGTH));

        var lines = await ReadLinesAsync(logs, 3);

        Assert.That(lines[0].Dto.ExceptionAt, Is.EqualTo(8));
        Assert.That(lines[0].Dto.Text[8], Is.EqualTo('\n'));

        var redacted = $"failed {LogStreamService.REDACTED}\nagain";
        Assert.That(lines[1].Dto.ExceptionAt, Is.EqualTo(redacted.Length));
        Assert.That(lines[1].Dto.Text, Does.StartWith(redacted + "\nSystem.InvalidOperationException: boom " + LogStreamService.REDACTED));

        Assert.That(lines[2].Dto.ExceptionAt, Is.Null);
        Assert.That(lines[2].Raw, Does.Not.Contain("exceptionAt"));
        Assert.That(lines[2].Dto.Props!["Long"], Has.Length.EqualTo(LogStreamService.MAX_PROP_LENGTH));
    }

    [Test]
    public async Task Props_MatchTheTextAndStayWithinTheLimits()
    {
        using var logs = new LogStreamService(TimeProvider.System);
        logs.AddSecrets(TOKEN, API_KEY);
        using var logger = new LoggerConfiguration().WriteTo.Sink(logs.Sink).CreateLogger();

        const string awkward = "a\r\nb\"c\\d\te";
        logger.Information("m {Body} {Pad,6} {@Point} {0}", awkward, "x", new { X = 1, Y = "z" }, 7);
        logger.Information(new string('t', LogStreamService.MAX_TEMPLATE_LENGTH + 1));
        var many = Enumerable.Range(0, LogStreamService.MAX_PROPS + 5).Aggregate((ILogger)logger, static (l, i) => l.ForContext("P" + i, i));
        many.Information("many");
        var longValue = new string('v', LogStreamService.MAX_PROP_LENGTH);
        logger.Information("{A} {B} {C} {D} {E} {F} {G} {H} {I}", longValue, longValue, longValue, longValue, longValue,
            longValue, longValue, longValue, longValue);
        const string secretName = "HiddenProxyPassword";
        logs.AddSecret(secretName);
        logger.Information("{" + secretName + "} {Cut}", 1, new string('c', LogStreamService.MAX_PROP_LENGTH - 3) + TOKEN);

        var lines = await ReadLinesAsync(logs, 5);

        var props = lines[0].Dto.Props!;
        Assert.That(props["Body"], Is.EqualTo("a\nb\"c\\d\te"));
        Assert.That(props["Pad"], Is.EqualTo("     x"));
        Assert.That(lines[0].Dto.Text, Is.EqualTo($"m {props["Body"]} {props["Pad"]} {props["Point"]} {props["0"]}"));

        Assert.That(lines[1].Dto.Template, Is.Null);
        Assert.That(lines[1].Raw, Does.Not.Contain("template"));

        Assert.That(lines[2].Dto.Props, Has.Count.EqualTo(LogStreamService.MAX_PROPS));

        Assert.That(lines[3].Dto.Props!.Values.Sum(static v => v.Length), Is.LessThanOrEqualTo(LogStreamService.MAX_PROPS_CHARS));

        Assert.That(lines[4].Dto.Props!.Keys, Is.EquivalentTo(new[] { "Cut" }));
        Assert.That(lines[4].Raw, Does.Not.Contain(secretName));
        Assert.That(lines[4].Dto.Props!["Cut"], Does.Not.Contain("[redac").And.Not.EndWith("["));
        Assert.That(lines[4].Dto.Props!["Cut"], Has.Length.LessThanOrEqualTo(LogStreamService.MAX_PROP_LENGTH));
    }

    private static async Task<(LogLineDto Dto, string Raw)[]> ReadLinesAsync(LogStreamService logs, int count)
    {
        await WaitForLinesAsync(logs, count);
        var frames = new FeedFrame[count];
        Assert.That(logs.Tail(count, frames, out _), Is.EqualTo(count));
        return Array.ConvertAll(frames, static f =>
        {
            var raw = Encoding.UTF8.GetString(f.Bytes);
            return (Parse(f), raw);
        });
    }

    public static async Task WaitForLinesAsync(LogStreamService logs, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Newest(logs) < count)
            await Task.Delay(10, cts.Token);
    }

    private static long Newest(LogStreamService logs)
    {
        logs.Tail(0, [], out var newest);
        return newest;
    }

    private static LogLineDto Parse(FeedFrame frame)
    {
        var text = Encoding.UTF8.GetString(frame.Bytes);
        Assert.That(text, Does.StartWith("data: ").And.EndWith("\n\n"));
        return JsonSerializer.Deserialize(text["data: ".Length..^2], ApiJsonContext.Default.LogLineDto)!;
    }
}
