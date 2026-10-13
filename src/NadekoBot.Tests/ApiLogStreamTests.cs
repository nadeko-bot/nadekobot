#nullable enable
using System;
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
        for (var i = 0; i < written; i++)
            logger.Information("line {Index}", i);

        await WaitForLinesAsync(logs, written);

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
