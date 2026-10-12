#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NadekoBot.Modules.Music;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

public sealed class MusicNetworkTests
{
    [Test]
    public void TryParseRequest_AcceptsOnlyConnectOrAbsoluteHttp()
    {
        Assert.That(PublicOnlyProxy.TryParseRequest("CONNECT example.com:443 HTTP/1.1\r\n\r\n"u8, out var connect), Is.True);
        Assert.That(connect, Is.EqualTo(new PublicOnlyProxy.ProxyRequest(true, "example.com", 443, "CONNECT", string.Empty)));

        Assert.That(PublicOnlyProxy.TryParseRequest("CONNECT [::1]:8080 HTTP/1.1\r\n\r\n"u8, out var v6), Is.True);
        Assert.That((v6.Host, v6.Port), Is.EqualTo(("::1", 8080)));

        Assert.That(PublicOnlyProxy.TryParseRequest("GET http://example.com/a?b=1 HTTP/1.1\r\n\r\n"u8, out var get), Is.True);
        Assert.That(get, Is.EqualTo(new PublicOnlyProxy.ProxyRequest(false, "example.com", 80, "GET", "/a?b=1")));

        string[] refused =
        [
            "GET /relative HTTP/1.1\r\n\r\n",
            "GET https://example.com/ HTTP/1.1\r\n\r\n",
            "GET file:///etc/passwd HTTP/1.1\r\n\r\n",
            "CONNECT example.com HTTP/1.1\r\n\r\n",
            "CONNECT example.com:0 HTTP/1.1\r\n\r\n",
            "CONNECT example.com:70000 HTTP/1.1\r\n\r\n",
            "CONNECT example.com:443 SPDY/3\r\n\r\n",
            "no line end"
        ];
        foreach (var head in refused)
            Assert.That(PublicOnlyProxy.TryParseRequest(Encoding.ASCII.GetBytes(head), out _), Is.False, head);
    }

    [Test]
    public async Task Proxy_ConnectsOnlyToAllowedAddressesAndDropsProxyHeaders()
    {
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
        var received = ServeOnceInternalAsync(origin);

        using var refusing = PublicOnlyProxy.WithAddressCheck(static ip => !IPAddress.IsLoopback(ip));
        Assert.That(refusing.TryGetUrl(out var refusingUrl), Is.True);
        Assert.That(await SendInternalAsync(refusingUrl, $"CONNECT 127.0.0.1:{originPort} HTTP/1.1\r\n\r\n"),
            Does.StartWith("HTTP/1.1 403"));
        Assert.That(await SendInternalAsync(refusingUrl, $"GET http://localhost:{originPort}/ HTTP/1.1\r\n\r\n"),
            Does.StartWith("HTTP/1.1 403"));

        using var allowing = PublicOnlyProxy.WithAddressCheck(static _ => true);
        Assert.That(allowing.TryGetUrl(out var allowingUrl), Is.True);
        var response = await SendInternalAsync(allowingUrl,
            $"GET http://127.0.0.1:{originPort}/song?x=1 HTTP/1.1\r\nHost: 127.0.0.1\r\n"
            + "Proxy-Authorization: Basic c2VjcmV0\r\nConnection: keep-alive\r\n\r\n");

        Assert.That(response, Does.EndWith("ok"));
        var request = await received;
        Assert.That(request, Does.StartWith("GET /song?x=1 HTTP/1.1\r\n"));
        Assert.That(request, Does.Contain("Connection: close"));
        Assert.That(request, Does.Not.Contain("Proxy-Authorization"));
        Assert.That(request, Does.Not.Contain("keep-alive"));
    }

    [Test]
    public void CacheKey_IsAFileNameInsideTheCacheForAnyTrackId()
    {
        Assert.That(AudioFileCacheService.GetCacheKey("dQw4w9WgXcQ", MusicPlatform.Youtube), Is.EqualTo("yt/dQw4w9WgXcQ"));

        string[] hostile = ["../../../creds", "..", "a/b", "a\\b", "/etc/passwd", "", new string('a', 65)];
        foreach (var id in hostile)
        {
            var key = AudioFileCacheService.GetCacheKey(id, MusicPlatform.Youtube);
            Assert.That(key, Does.Match("^ytx/[0-9a-f]{64}$"), id);
        }

        Assert.That(AudioFileCacheService.GetCacheKey("dQw4w9WgXcQ", MusicPlatform.Local), Does.Match("^other/[0-9a-f]{64}$"));
        Assert.That(AudioFileCacheService.GetCacheKey("a", MusicPlatform.Youtube),
            Is.Not.EqualTo(AudioFileCacheService.GetCacheKey("a", MusicPlatform.Local)));
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

    private static async Task<string> ServeOnceInternalAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await using var stream = client.GetStream();
        var buf = new byte[4096];
        var total = 0;
        while (total < buf.Length)
        {
            var read = await stream.ReadAsync(buf.AsMemory(total));
            if (read == 0)
                break;

            total += read;
            if (Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }

        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
        return Encoding.ASCII.GetString(buf, 0, total);
    }
}
