#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class YoutubeMusicTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-yt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
        => Directory.Delete(_dir, recursive: true);

    [Test]
    public void Hosts_MatchOnlyYoutubeNamesOnWebPorts()
    {
        foreach (var host in new[]
                 {
                     "youtube.com", "www.youtube.com", "m.YouTube.com", "youtu.be", "rr3---sn-abc.googlevideo.com",
                     "www.youtube-nocookie.com", "youtubei.googleapis.com", "www.youtube.com."
                 })
            Assert.That(YoutubeHosts.Matches(host, 443), Is.True, host);

        foreach (var host in new[]
                 {
                     "notyoutube.com", "youtube.com.evil.net", "googleapis.com", "soundcloud.com", "142.250.1.1",
                     "evilgooglevideo.com"
                 })
            Assert.That(YoutubeHosts.Matches(host, 443), Is.False, host);

        Assert.That(YoutubeHosts.Matches("www.youtube.com", 80), Is.True);
        Assert.That(YoutubeHosts.Matches("www.youtube.com", 8080), Is.False);
    }

    [Test]
    public void UpstreamProxy_TakesOnlyAPlainHttpProxyUrl()
    {
        Assert.That(UpstreamProxy.TryParse("http://1.2.3.4:8080/", out var plain), Is.True);
        Assert.That((plain.EndPoint.Host, plain.EndPoint.Port, plain.Authorization), Is.EqualTo(("1.2.3.4", 8080, (string?)null)));

        Assert.That(UpstreamProxy.TryParse("http://us%40er:p%3Ass@proxy.example:80", out var auth), Is.True);
        Assert.That(auth.Password, Is.EqualTo("p:ss"));
        Assert.That(auth.Authorization, Is.EqualTo("Basic " + Convert.ToBase64String("us@er:p:ss"u8.ToArray())));

        foreach (var bad in new[]
                 {
                     null, "", "https://1.2.3.4:8080/", "socks5://1.2.3.4:1080", "http://1.2.3.4:8080/x",
                     "http://1.2.3.4:8080/?a=1", "http://1.2.3.4:8080/#f", "http://u:p ss@1.2.3.4/", "1.2.3.4:8080",
                     "http://" + new string('a', 600) + ".com/"
                 })
            Assert.That(UpstreamProxy.TryParse(bad, out _), Is.False, bad);
    }

    [Test]
    public void Setting_ReadsTheFileAgainWhenItChanges()
    {
        var setting = new YoutubeUpstreamSetting(Path.Combine(_dir, ".youtube-proxy"));
        Assert.That(setting.Current, Is.Null);

        setting.Set("a", "http://1.2.3.4:1/");
        Assert.That(setting.Id, Is.EqualTo("a"));

        setting.Set("bb", "http://u:p@5.6.7.8:2/");
        Assert.That((setting.Id, setting.Proxy?.EndPoint.Port), Is.EqualTo(("bb", 2)));

        File.WriteAllText(setting.FilePath, "id=c\nurl=https://bad/\n");
        Assert.That(setting.Current, Is.Null, "a bad file means no proxy");

        setting.Clear();
        Assert.That(setting.Current, Is.Null);
    }

    [Test]
    public async Task Proxy_SendsYoutubeThroughTheUpstreamByNameWithAuth()
    {
        using var upstream = new TcpListener(IPAddress.Loopback, 0);
        upstream.Start();
        var served = ServeTunnelInternalAsync(upstream);

        var setting = new YoutubeUpstreamSetting(Path.Combine(_dir, ".youtube-proxy"));
        setting.Set("p1", $"http://user:pw@127.0.0.1:{((IPEndPoint)upstream.LocalEndpoint).Port}/");
        var stats = new YoutubeStats(TimeProvider.System);

        // Nothing else is allowed, so a direct connection would get 403.
        using var proxy = PublicOnlyProxy.ForTests(IPAddress.IsLoopback, null, setting, stats);
        Assert.That(proxy.HasYoutubeUpstream, Is.True);
        Assert.That(proxy.TryGetUrl(out var url), Is.True);

        var response = await SendInternalAsync(url, "GET http://www.youtube.com/watch?v=x HTTP/1.1\r\nHost: www.youtube.com\r\n\r\n");
        Assert.That(response, Does.EndWith("ok"));

        var (connect, inner) = await served;
        Assert.That(connect, Does.StartWith("CONNECT www.youtube.com:80 HTTP/1.1\r\n"));
        Assert.That(connect, Does.Contain("Proxy-Authorization: Basic " + Convert.ToBase64String("user:pw"u8.ToArray())));
        Assert.That(inner, Does.StartWith("GET /watch?v=x HTTP/1.1\r\n"));
        Assert.That(stats.LastHour().ProxyBytes, Is.GreaterThan(0));

        // Another host never goes through the YouTube proxy.
        var other = await SendInternalAsync(url, "CONNECT 127.0.0.2:443 HTTP/1.1\r\n\r\n");
        Assert.That(other, Does.Not.StartWith("HTTP/1.1 200"));
    }

    [Test]
    public async Task Proxy_GivesBadGatewayAndCountsItWhenTheUpstreamFails()
    {
        // A port with no listener.
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();

        var setting = new YoutubeUpstreamSetting(Path.Combine(_dir, ".youtube-proxy"));
        setting.Set("p1", $"http://127.0.0.1:{port}/");
        var stats = new YoutubeStats(TimeProvider.System);
        using var proxy = PublicOnlyProxy.ForTests(IPAddress.IsLoopback, null, setting, stats);
        Assert.That(proxy.TryGetUrl(out var url), Is.True);

        var response = await SendInternalAsync(url, "CONNECT rr1.googlevideo.com:443 HTTP/1.1\r\n\r\n");
        Assert.That(response, Does.StartWith("HTTP/1.1 502"));
        Assert.That(stats.LastHour().ProxyErrors, Is.EqualTo(1));
    }

    [Test]
    public async Task Proxy_WithoutASettingTreatsYoutubeLikeAnyHost()
    {
        var setting = new YoutubeUpstreamSetting(Path.Combine(_dir, ".youtube-proxy"));
        var stats = new YoutubeStats(TimeProvider.System);
        using var proxy = PublicOnlyProxy.ForTests(static _ => false, null, setting, stats);
        Assert.That(proxy.HasYoutubeUpstream, Is.False);
        Assert.That(proxy.TryGetUrl(out var url), Is.True);

        var response = await SendInternalAsync(url, "CONNECT www.youtube.com:443 HTTP/1.1\r\n\r\n");
        Assert.That(response, Does.StartWith("HTTP/1.1 403"), "the address check, not the YouTube proxy");
        Assert.That(stats.LastHour().ProxyErrors, Is.Zero);
    }

    [Test]
    public void Classify_ReadsYtDlpErrors()
    {
        Assert.That(YoutubeStats.Classify("", true), Is.EqualTo(((YoutubeOutcome?)YoutubeOutcome.Ok, false)));
        Assert.That(YoutubeStats.Classify("", false), Is.EqualTo(((YoutubeOutcome?)null, false)));
        Assert.That(YoutubeStats.Classify("ERROR: [youtube] x: Sign in to confirm you’re not a bot.", false).Outcome,
            Is.EqualTo(YoutubeOutcome.BotCheck));
        Assert.That(YoutubeStats.Classify("ERROR: unable to download: HTTP Error 429: Too Many Requests", false).Outcome,
            Is.EqualTo(YoutubeOutcome.RateLimited));
        Assert.That(YoutubeStats.Classify("ERROR: HTTP Error 403: Forbidden", false).Outcome,
            Is.EqualTo(YoutubeOutcome.Forbidden));
        Assert.That(YoutubeStats.Classify("ERROR: Unable to connect to proxy", false).Outcome, Is.Null);
        Assert.That(YoutubeStats.Classify("ERROR: something else", false).Outcome, Is.EqualTo(YoutubeOutcome.OtherError));
        Assert.That(
            YoutubeStats.Classify("WARNING: [youtube] No supported JavaScript runtime could be found.", true),
            Is.EqualTo(((YoutubeOutcome?)YoutubeOutcome.Ok, true)));
    }

    [Test]
    public void Stats_KeepTheLastHourAndDay()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
        var stats = new YoutubeStats(time);
        stats.Record(YoutubeOutcome.Ok);
        stats.Record(YoutubeOutcome.BotCheck);
        stats.AddProxyBytes(100);

        time.Advance(TimeSpan.FromMinutes(70));
        stats.Record(YoutubeOutcome.Ok);
        Assert.That(stats.LastHour(), Is.EqualTo(new YoutubeCounts(1, 0, 0, 0, 0, 0, 0, 0)));
        Assert.That(stats.LastDay(), Is.EqualTo(new YoutubeCounts(2, 1, 0, 0, 0, 0, 0, 100)));

        time.Advance(TimeSpan.FromHours(24));
        Assert.That(stats.LastDay(), Is.EqualTo(default(YoutubeCounts)));
    }

    [Test]
    public void MediaTools_UseThePathUntilAToolIsInstalled()
    {
        var tools = new MediaTools(Path.Combine(_dir, "tools"));
        Assert.That((tools.YtDlp, tools.Ffmpeg, tools.Deno), Is.EqualTo(("yt-dlp", "ffmpeg", (string?)null)));

        var dir = Path.Combine(tools.ToolDir("ffmpeg"), "8.1-abcdef12");
        Directory.CreateDirectory(dir);
        var manifest = new ToolManifest("ffmpeg", "8.1", "sha256:" + new string('a', 64), "8.1-abcdef12", 1);
        File.WriteAllText(Path.Combine(tools.ToolDir("ffmpeg"), MediaTools.CURRENT_FILE), MediaTools.Format(manifest));
        Assert.That(tools.Ffmpeg, Is.EqualTo("ffmpeg"), "a current file without the program is ignored");

        File.WriteAllText(Path.Combine(dir, "ffmpeg"), "x");
        File.WriteAllText(Path.Combine(tools.ToolDir("ffmpeg"), MediaTools.CURRENT_FILE),
            MediaTools.Format(manifest with { InstalledAt = 22 }));
        Assert.That(tools.Ffmpeg, Is.EqualTo(Path.Combine(dir, "ffmpeg")));

        Assert.That(MediaTools.Parse("ffmpeg", "version=1\ndigest=sha256:" + new string('a', 64) + "\ndir=../x\n"),
            Is.Null);
    }

    private static async Task<string> SendInternalAsync(string proxyUrl, string head)
    {
        var uri = new Uri(proxyUrl);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    // A proxy which opens the tunnel, then answers the request inside it.
    private static async Task<(string Connect, string Inner)> ServeTunnelInternalAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await using var stream = client.GetStream();
        var connect = await ReadHeadInternalAsync(stream);
        await stream.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray());
        var inner = await ReadHeadInternalAsync(stream);
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        return (connect, inner);
    }

    private static async Task<string> ReadHeadInternalAsync(Stream stream)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one).AsTask().WaitAsync(TimeSpan.FromSeconds(10)) == 0)
                break;

            sb.Append((char)one[0]);
        }

        return sb.ToString();
    }
}
