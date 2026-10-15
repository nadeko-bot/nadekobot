#nullable enable
using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using NadekoBot.Hosted;
using NadekoBot.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiToolTests
{
    private const string API_KEY = "placeholder-key-xxxxxxxxxxxxxxxxxxxx";
    private const string VERSION = "2026.10.10.233141";
    private const string DIGEST = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private string _dir = null!;
    private HostPaths _paths = null!;
    private WebApplication? _app;
    private HttpClient? _http;

    [SetUp]
    public async Task SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-tools-" + Guid.NewGuid().ToString("N"));
        _paths = new HostPaths(Path.Combine(_dir, "data"));
        Directory.CreateDirectory(_paths.DataDir);

        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.NoToken);
        var settings = new ApiSettings { Port = 0, MaxUpdateMb = ApiSettings.MIN_UPDATE_MB }.WithKey(API_KEY);
        _app = ApiHost.Build(new HostContext(HostMode.Standalone, 0, 1),
            _paths,
            settings,
            bridge,
            new RestoreTarget(Path.Combine(_paths.DataDir, "NadekoBot.db")));

        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);
    }

    [TearDown]
    public async Task TearDown()
    {
        _http?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();

        Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task Tools_NeedTheKey()
    {
        using var anonymous = new HttpClient { BaseAddress = _http!.BaseAddress };
        using var list = await anonymous.GetAsync("/api/tools");
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var proxy = await anonymous.GetAsync("/api/youtube");
        Assert.That(proxy.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Install_MakesTheToolCurrentAndKeepsOnePreviousVersion()
    {
        var tools = new MediaTools(_paths.ToolsDir);
        Assert.That(tools.YtDlp, Is.EqualTo("yt-dlp"), "no install: the program on PATH");

        using (var res = await _http!.SendAsync(InstallRequest("yt-dlp", Package("yt-dlp", "#!/bin/sh\necho 1.0\n"), "1"))
            )
        {
            Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var tool = await res.Content.ReadFromJsonAsync(ApiJsonContext.Default.ToolDto);
            Assert.That((tool!.Name, tool.Version, tool.Digest), Is.EqualTo(("yt-dlp", "1", DIGEST)));
        }

        var first = tools.YtDlp;
        Assert.That(first, Does.StartWith(_paths.ToolsDir));
        Assert.That(File.Exists(first), Is.True);

        using (var second = await _http.SendAsync(InstallRequest("yt-dlp", Package("yt-dlp", "#!/bin/sh\necho 2.0\n"), "2",
                   DIGEST.Replace("0123", "aaaa"))))
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (var third = await _http.SendAsync(InstallRequest("yt-dlp", Package("yt-dlp", "#!/bin/sh\necho 3.0\n"), "3",
                   DIGEST.Replace("0123", "bbbb"))))
            Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        Assert.That(tools.Manifest("yt-dlp")?.Version, Is.EqualTo("3"));
        Assert.That(File.Exists(first), Is.False, "only the current and the previous version stay");
        Assert.That(Directory.GetDirectories(tools.ToolDir("yt-dlp")), Has.Length.EqualTo(2));

        var list = await _http.GetFromJsonAsync("/api/tools", ApiJsonContext.Default.ToolListDto);
        Assert.That(list!.Tools.Select(static t => t.Name), Is.EqualTo(new[] { "yt-dlp" }));

        using (var removed = await _http.DeleteAsync("/api/tools/yt-dlp"))
            Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That(tools.YtDlp, Is.EqualTo("yt-dlp"));
    }

    [Test]
    public async Task Install_ChecksEachToolInThisSandbox()
    {
        using (var deno = await _http!.SendAsync(InstallRequest("deno", Package("deno", "#!/bin/sh\necho 2\n"))))
            Assert.That(deno.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (var ffmpeg = await _http.SendAsync(InstallRequest("ffmpeg",
                   Package("ffmpeg", "#!/bin/sh\nprintf 'Supported file protocols:\\nInput:\\n  file\\n  https\\n'\n"))))
            Assert.That(ffmpeg.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using (var noHttps = await _http.SendAsync(InstallRequest("ffmpeg",
                   Package("ffmpeg", "#!/bin/sh\necho file\n"),
                   "2",
                   DIGEST.Replace("0123", "cccc"))))
            await AssertErrorAsync(noHttps, HttpStatusCode.BadRequest, "tool_check_failed");

        using (var failing = await _http.SendAsync(InstallRequest("yt-dlp", Package("yt-dlp", "#!/bin/sh\nexit 1\n"))))
            await AssertErrorAsync(failing, HttpStatusCode.BadRequest, "tool_check_failed");

        var tools = new MediaTools(_paths.ToolsDir);
        Assert.That(tools.Deno, Is.Not.Null);
        Assert.That(tools.Manifest("ffmpeg")?.Version, Is.EqualTo(VERSION), "a failed check keeps the current tool");
        Assert.That(tools.Manifest("yt-dlp"), Is.Null);
        Assert.That(Directory.GetFileSystemEntries(tools.ToolDir("yt-dlp")), Is.Empty, "no leftovers");
    }

    [Test]
    public async Task Install_RefusesBadRequests()
    {
        var package = Package("yt-dlp", "#!/bin/sh\necho 1\n");

        using (var unknown = await _http!.SendAsync(InstallRequest("bash", package)))
            await AssertErrorAsync(unknown, HttpStatusCode.NotFound, "not_found");

        using (var wrongSha = await _http.SendAsync(InstallRequest("yt-dlp", package, sha256: new string('0', 64))))
            await AssertErrorAsync(wrongSha, HttpStatusCode.BadRequest, "checksum_mismatch");

        foreach (var version in new[] { "", "../x", ".hidden", "-x", "a/b", new string('1', 65) })
        {
            using var bad = await _http.SendAsync(InstallRequest("yt-dlp", package, version));
            await AssertErrorAsync(bad, HttpStatusCode.BadRequest, "bad_request");
        }

        foreach (var digest in new[] { "", "sha256:abc", "md5:" + new string('0', 64), DIGEST.ToUpperInvariant() })
        {
            using var bad = await _http.SendAsync(InstallRequest("yt-dlp", package, digest: digest));
            await AssertErrorAsync(bad, HttpStatusCode.BadRequest, "bad_request");
        }

        using (var otherName = await _http.SendAsync(InstallRequest("deno", package)))
            await AssertErrorAsync(otherName, HttpStatusCode.BadRequest, "bad_package");

        using (var removeUnknown = await _http.DeleteAsync("/api/tools/bash"))
            Assert.That(removeUnknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task YoutubeProxy_IsStoredPrivatelyAndNeverReturned()
    {
        var http = _http!;
        var status = await http.GetFromJsonAsync("/api/youtube", ApiJsonContext.Default.YoutubeStatusDto);
        Assert.That(status!.ProxyId, Is.Null);

        foreach (var body in new[]
                 {
                     """{"id":"p1","url":"https://u:p@1.2.3.4:8080/"}""",
                     """{"id":"p1","url":"http://1.2.3.4:8080/path"}""",
                     """{"id":"p 1","url":"http://1.2.3.4:8080/"}""",
                     """{"id":"","url":"http://1.2.3.4:8080/"}""",
                     """{"id":null,"url":"http://1.2.3.4:8080/"}"""
                 })
        {
            using var bad = await http.PutAsync("/api/youtube/proxy", Json(body));
            await AssertErrorAsync(bad, HttpStatusCode.BadRequest, "invalid_value");
        }

        // A body without a field does not bind.
        foreach (var body in new[] { """{"url":"http://1.2.3.4:8080/"}""", """{"id":"p1"}""" })
        {
            using var bad = await http.PutAsync("/api/youtube/proxy", Json(body));
            await AssertErrorAsync(bad, HttpStatusCode.BadRequest, "bad_request");
        }

        using (var set = await http.PutAsync("/api/youtube/proxy",
                   Json("""{"id":"ws-17","url":"http://user:secret-pass@1.2.3.4:8080/"}""")))
            Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That(File.GetUnixFileMode(_paths.YoutubeProxy),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));

        using (var get = await http.GetAsync("/api/youtube"))
        {
            var text = await get.Content.ReadAsStringAsync();
            Assert.That(text, Does.Contain("\"proxyId\":\"ws-17\""));
            Assert.That(text, Does.Not.Contain("secret-pass"));
            Assert.That(text, Does.Contain("\"hour\":{").And.Contain("\"proxyBytes\":"));
        }

        var setting = new YoutubeUpstreamSetting(_paths.YoutubeProxy);
        Assert.That(setting.Proxy?.EndPoint.Host, Is.EqualTo("1.2.3.4"));
        Assert.That(setting.Proxy?.Authorization,
            Is.EqualTo("Basic " + Convert.ToBase64String("user:secret-pass"u8.ToArray())));

        using (var clear = await http.DeleteAsync("/api/youtube/proxy"))
            Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That(File.Exists(_paths.YoutubeProxy), Is.False);
        Assert.That(setting.Proxy, Is.Null);
    }

    private static StringContent Json(string body)
        => new(body, Encoding.UTF8, "application/json");

    private static HttpRequestMessage InstallRequest(
        string name,
        byte[] package,
        string version = VERSION,
        string digest = DIGEST,
        string? sha256 = null)
    {
        var content = new ByteArrayContent(package);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/tools/" + name) { Content = content };
        req.Headers.Add("X-Content-Sha256", sha256 ?? Convert.ToHexStringLower(SHA256.HashData(package)));
        req.Headers.Add("X-Tool-Version", version);
        req.Headers.Add("X-Tool-Digest", digest);
        return req;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.That(res.StatusCode, Is.EqualTo(status));
        var body = JsonSerializer.Deserialize(await res.Content.ReadAsStringAsync(), ApiJsonContext.Default.ErrorDto);
        Assert.That(body?.Error, Is.EqualTo(code));
    }

    // Mode 0644: the package must make the tool runnable itself.
    private static byte[] Package(string executable, string script)
    {
        const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
                                  | UnixFileMode.OtherRead;
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "tool/")
            {
                Mode = mode | UnixFileMode.UserExecute
            });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "tool/" + executable)
            {
                Mode = mode,
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(script))
            });
        }

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(tar.ToArray());

        return ms.ToArray();
    }
}
