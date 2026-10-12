#nullable enable
using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Hosted;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiUpdateTests
{
    private const string API_KEY = "placeholder-key-xxxxxxxxxxxxxxxxxxxx";
    private const string TOP = "nadeko-hosted-linux-x64";
    private const string VERSION = "9.9.9";
    private const string VERSION_SCRIPT = "#!/bin/sh\necho " + VERSION + "\n";

    private const UnixFileMode FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite
                                                                 | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private string _dir = null!;
    private HostPaths _paths = null!;
    private WebApplication? _app;
    private HttpClient? _http;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-update-" + Guid.NewGuid().ToString("N"));
        _paths = new HostPaths(Path.Combine(_dir, "data"));
        Directory.CreateDirectory(_paths.DataDir);
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
    public async Task Update_NeedsTheKeyAGzipBodyAndValidHeaders()
    {
        await StartAsync();
        var package = Package(ValidEntries());

        using (var noKey = await _http!.SendAsync(UpdateRequest(package)))
            await AssertErrorAsync(noKey, HttpStatusCode.Unauthorized, "unauthorized");

        Authorize();

        using (var notGzip = await _http.SendAsync(UpdateRequest(package, contentType: "application/zip")))
            await AssertErrorAsync(notGzip, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");

        using (var noSha = await _http.SendAsync(UpdateRequest(package, sha256: null)))
            await AssertErrorAsync(noSha, HttpStatusCode.BadRequest, "bad_request");

        using (var shortSha = await _http.SendAsync(UpdateRequest(package, sha256: new string('a', 63))))
            await AssertErrorAsync(shortSha, HttpStatusCode.BadRequest, "bad_request");

        foreach (var version in new[] { "", "1.2", "1.2.3.4", "v1.2.3", "1.2.3-beta", "1.2.x" })
        {
            using var badVersion = await _http.SendAsync(UpdateRequest(package, version: version));
            await AssertErrorAsync(badVersion, HttpStatusCode.BadRequest, "bad_request");
        }

        // Versions without PUT /api/update would leave the bot with no way to update again.
        using (var tooOld = await _http.SendAsync(UpdateRequest(package, version: "7.5.9")))
            await AssertErrorAsync(tooOld, HttpStatusCode.BadRequest, "invalid_value");

        AssertNothingStaged();
    }

    [Test]
    public async Task Extract_CountsHardLinksAsCopies()
    {
        var content = new string('x', 100);
        var package = Path.Combine(_dir, "package.tar.gz");
        System.IO.File.WriteAllBytes(package, Package([
            Dir(TOP),
            File($"{TOP}/NadekoBot.Hosted", VERSION_SCRIPT),
            File($"{TOP}/big.bin", content),
            Link(TarEntryType.HardLink, $"{TOP}/big2.bin", $"{TOP}/big.bin")
        ]));
        var fits = VERSION_SCRIPT.Length + 2 * content.Length;
        var fitsOnDisk = 3 * UpdatePackage.DISK_BLOCK;

        Assert.ThrowsAsync<UpdatePackage.InvalidPackageException>(()
            => UpdatePackage.ExtractAsync(package, Path.Combine(_dir, "a"), fits - 1, long.MaxValue, CancellationToken.None));
        Assert.ThrowsAsync<UpdatePackage.NoSpaceException>(()
            => UpdatePackage.ExtractAsync(package, Path.Combine(_dir, "b"), long.MaxValue, fitsOnDisk - 1, CancellationToken.None));
        await UpdatePackage.ExtractAsync(package, Path.Combine(_dir, "c"), fits, fitsOnDisk, CancellationToken.None);
        Assert.That(System.IO.File.ReadAllText(Path.Combine(_dir, "c", "big2.bin")), Is.EqualTo(content));
    }

    [Test]
    public async Task Update_RejectsABodyAboveMaxUpdateMb()
    {
        await StartAsync();
        Authorize();
        var limit = (long)ApiSettings.MIN_UPDATE_MB * 1024 * 1024;

        // Expect: 100-continue, so the client sends nothing once the server has answered from the length alone.
        using (var declared = UpdateRequest(new LazyContent(limit + 1, knownLength: true), new string('0', 64)))
        {
            declared.Headers.ExpectContinue = true;
            using var res = await _http!.SendAsync(declared);
            await AssertErrorAsync(res, HttpStatusCode.RequestEntityTooLarge, "too_large");
        }

        // Without a length the limit is enforced while the body is read.
        using (var chunked = await _http!.SendAsync(UpdateRequest(new LazyContent(limit + 1, knownLength: false), new string('0', 64))))
            await AssertErrorAsync(chunked, HttpStatusCode.RequestEntityTooLarge, "too_large");

        AssertNothingStaged();
    }

    [Test]
    public async Task Update_RejectsAWrongChecksumOrAnUnsafeOrIncompletePackage()
    {
        await StartAsync();
        Authorize();

        using (var wrongSha = await _http!.SendAsync(UpdateRequest(Package(ValidEntries()), sha256: new string('0', 64))))
            await AssertErrorAsync(wrongSha, HttpStatusCode.BadRequest, "checksum_mismatch");

        AssertNothingStaged();

        var packages = new[]
        {
            Package(ValidEntries().Append(File($"{TOP}/../escape.txt", "x"))),
            Package(ValidEntries().Append(File("/etc/evil", "x"))),
            Package([Dir(TOP), File($"{TOP}/NadekoBot.dll", "dll")]),
            Package(ValidEntries().Append(File("other/file.txt", "x"))),
            Package(ValidEntries().Append(Link(TarEntryType.SymbolicLink, $"{TOP}/out", "../../outside"))),
            Package(ValidEntries().Append(Link(TarEntryType.SymbolicLink, $"{TOP}/abs", "/etc/passwd"))),
            Package(ValidEntries().Append(Link(TarEntryType.HardLink, $"{TOP}/hard", "/etc/passwd"))),
            Package(ValidEntries().Append(new PaxTarEntry(TarEntryType.Fifo, $"{TOP}/fifo"))),
            Package(ValidEntries().Append(File($"{TOP}/NadekoBot.Hosted", "dup"))),
            Package(ValidEntries().Append(Link(TarEntryType.SymbolicLink, $"{TOP}/alias", "strings"))
                .Append(File($"{TOP}/alias/en-US.yml", "clash"))),
            Gzip(Encoding.UTF8.GetBytes("not a tar at all, but long enough to be read as one block or more"))
        };

        foreach (var package in packages)
        {
            using var res = await _http.SendAsync(UpdateRequest(package));
            await AssertErrorAsync(res, HttpStatusCode.BadRequest, "bad_package");
            AssertNothingStaged();
        }

        Assert.That(System.IO.File.Exists(Path.Combine(_dir, "escape.txt")), Is.False);
        Assert.That(System.IO.File.Exists(Path.Combine(_paths.DataDir, ".update", "escape.txt")), Is.False);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task Update_RejectsAPackageWhichReportsAnotherVersion()
    {
        await StartAsync();
        Authorize();

        using var res = await _http!.SendAsync(UpdateRequest(Package(ValidEntries()), version: "9.9.8"));
        await AssertErrorAsync(res, HttpStatusCode.BadRequest, "version_mismatch");
        AssertNothingStaged();
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform(Exclude = "Win")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Update_StagesThePackageWritesTheMarkerAndExits(bool linkedData)
    {
        if (linkedData)
            LinkDataDir();

        await StartAsync();
        Authorize();
        System.IO.File.WriteAllText(Path.Combine(_paths.DataDir, ".update-result"), "keep");
        var package = Package(ValidEntries());
        var sha = Sha256(package);

        using var res = await _http!.SendAsync(UpdateRequest(package, sha256: sha.ToUpperInvariant()));
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var body = JsonSerializer.Deserialize(await res.Content.ReadAsStringAsync(), ApiJsonContext.Default.UpdateAcceptedDto);
        Assert.That(body, Is.EqualTo(new UpdateAcceptedDto(VERSION)));

        Assert.That(System.IO.File.ReadAllText(_paths.UpdatePending), Is.EqualTo($"version={VERSION}\nsha256={sha}\n"));
        Assert.That(System.IO.File.Exists(_paths.UpdateUpload), Is.False);
        Assert.That(System.IO.File.ReadAllText(Path.Combine(_paths.DataDir, ".update-result")), Is.EqualTo("keep"));

        var exe = Path.Combine(_paths.UpdateStaged, "NadekoBot.Hosted");
        Assert.That(System.IO.File.GetUnixFileMode(exe) & UnixFileMode.UserExecute, Is.EqualTo(UnixFileMode.UserExecute));
        Assert.That(System.IO.File.ReadAllText(Path.Combine(_paths.UpdateStaged, "strings", "en-US.yml")), Is.EqualTo("hi"));
        Assert.That(System.IO.File.GetUnixFileMode(Path.Combine(_paths.UpdateStaged, "tool.sh")),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead));
        Assert.That(System.IO.File.ReadAllText(Path.Combine(_paths.UpdateStaged, "hard.dll")), Is.EqualTo("dll"));
        Assert.That(new FileInfo(Path.Combine(_paths.UpdateStaged, "link.dll")).LinkTarget, Is.EqualTo("NadekoBot.dll"));

        // The process exits after the reply, and nothing may touch the data before it does.
        await WaitForStopAsync();
        var backups = _app!.Services.GetRequiredService<BackupService>();
        Assert.That(backups.TryBeginUpdate()?.Error, Is.EqualTo("update_in_progress"));
    }

    [Test]
    public async Task Update_WaitsForTheUpdateTrialAndOtherBackupWork()
    {
        await StartAsync();
        Authorize();
        System.IO.File.WriteAllText(_paths.UpdateTrial, "version=9.9.8");

        using (var onTrial = await _http!.SendAsync(UpdateRequest(Package(ValidEntries()))))
            await AssertErrorAsync(onTrial, HttpStatusCode.Conflict, "update_trial_pending");

        AssertNothingStaged();
        Assert.That(System.IO.File.Exists(_paths.UpdateTrial), Is.True);
        System.IO.File.Delete(_paths.UpdateTrial);

        var backups = _app!.Services.GetRequiredService<BackupService>();
        Assert.That(backups.TryBeginUpdate(), Is.Null);
        using (var importing = await _http.PostAsync("/api/backups/import", ZipContent()))
            await AssertErrorAsync(importing, HttpStatusCode.Conflict, "update_in_progress");

        using (var updating = await _http.SendAsync(UpdateRequest(Package(ValidEntries()))))
            await AssertErrorAsync(updating, HttpStatusCode.Conflict, "update_in_progress");

        backups.EndUpdate();
        AssertNothingStaged();
    }

    [Test]
    public void BootGuard_MarkReadyEndsTheUpdateTrial()
    {
        var guard = new BootGuard(_paths, new FakeTimeProvider());
        System.IO.File.WriteAllText(_paths.UpdateTrial, "1");
        guard.TryBeginAttempt();

        guard.MarkReady();

        Assert.That(System.IO.File.Exists(_paths.UpdateTrial), Is.False);
        Assert.That(System.IO.File.Exists(_paths.BootAttempts), Is.False);
        Assert.DoesNotThrow(guard.MarkReady);
    }

    [Test]
    public void DiscardLeftovers_KeepsTheSupervisorsSnapshots()
    {
        var snapshot = Path.Combine(_paths.UpdateDir, "db-before", "NadekoBot.db");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
        Directory.CreateDirectory(_paths.UpdateStaged);
        System.IO.File.WriteAllText(snapshot, "db");
        System.IO.File.WriteAllText(_paths.UpdateUpload, "upload");
        System.IO.File.WriteAllText(_paths.UpdatePending, "version=9.9.9");

        UpdateService.DiscardLeftovers(_paths);

        AssertNothingStaged();
        Assert.That(System.IO.File.ReadAllText(snapshot), Is.EqualTo("db"));
    }

    [Test]
    public void UpdateResult_ParsesOnlyTheLinesTheApplyStepWrites()
    {
        (string Text, UpdateResultDto Result)[] valid =
        [
            ("applied:7.6.1", new UpdateResultDto("applied", "7.6.1", null)),
            ("applied:7.6.1\n", new UpdateResultDto("applied", "7.6.1", null)),
            ("rolled_back:7.6.1-beta+2\n", new UpdateResultDto("rolled_back", "7.6.1-beta+2", null)),
            ("failed:unknown:unsafe_path\n", new UpdateResultDto("failed", "unknown", "unsafe_path")),
            ("rollback_failed:7.6.1:no_previous_install\n", new UpdateResultDto("rollback_failed", "7.6.1", "no_previous_install"))
        ];

        foreach (var (text, result) in valid)
            Assert.That(UpdateResultFile.Parse(text), Is.EqualTo(result), text);

        string[] invalid =
        [
            "", "\n", "applied", "applied:", "applied:7.6.1:reason", "failed:7.6.1", "failed:7.6.1:",
            "failed:7.6.1:a:b", "Applied:7.6.1", "installed:7.6.1", "applied:7.6.1\n\n", "applied:7.6.1\r\n",
            "applied:7.6.1\nfailed:7.6.1:x", " applied:7.6.1", "applied:7.6.1 ", "applied:7.6/1", "applied:<b>x</b>",
            "applied:7.6.\u00e9"
        ];

        foreach (var text in invalid)
            Assert.That(UpdateResultFile.Parse(text), Is.Null, text);

        var max = new string('a', 64);
        Assert.That(UpdateResultFile.Parse($"failed:{max}:{max}"), Is.EqualTo(new UpdateResultDto("failed", max, max)));
        Assert.That(UpdateResultFile.Parse($"applied:{max}a"), Is.Null);
        Assert.That(UpdateResultFile.Parse($"failed:1:{max}a"), Is.Null);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void UpdateResult_ReadsOnlyASmallRegularFile()
    {
        var path = _paths.UpdateResult;
        Assert.That(UpdateResultFile.Read(path), Is.Null);

        System.IO.File.WriteAllText(path, "applied:7.6.1\n");
        Assert.That(UpdateResultFile.Read(path), Is.EqualTo(new UpdateResultDto("applied", "7.6.1", null)));

        System.IO.File.WriteAllText(path, "applied:7.6.1\n" + new string(' ', UpdateResultFile.MAX_BYTES));
        Assert.That(UpdateResultFile.Read(path), Is.Null);

        System.IO.File.WriteAllBytes(path, [0xFF, 0xFE, 0x00]);
        Assert.That(UpdateResultFile.Read(path), Is.Null);

        // A valid result behind a link is still refused.
        var target = Path.Combine(_dir, "elsewhere");
        System.IO.File.WriteAllText(target, "applied:7.6.1\n");
        System.IO.File.Delete(path);
        System.IO.File.CreateSymbolicLink(path, target);
        Assert.That(UpdateResultFile.Read(path), Is.Null);

        System.IO.File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.That(UpdateResultFile.Read(path), Is.Null);
    }

    private void LinkDataDir()
    {
        var volume = Path.Combine(_dir, "volume");
        Directory.CreateDirectory(volume);
        Directory.Delete(_paths.DataDir);
        Directory.CreateSymbolicLink(_paths.DataDir, volume);
    }

    private void AssertNothingStaged()
    {
        Assert.That(System.IO.File.Exists(_paths.UpdatePending), Is.False);
        Assert.That(System.IO.File.Exists(_paths.UpdateUpload), Is.False);
        Assert.That(Directory.Exists(_paths.UpdateStaged), Is.False);
    }

    private async Task WaitForStopAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopping = _app!.Lifetime.ApplicationStopping;
        while (!stopping.IsCancellationRequested)
            await Task.Delay(10, cts.Token);
    }

    private async Task StartAsync()
    {
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.NoToken);
        var settings = new ApiSettings { Port = 0, MaxUpdateMb = ApiSettings.MIN_UPDATE_MB }.WithKey(API_KEY);
        _app = ApiHost.Build(
            new HostContext(HostMode.Standalone, 0, 1),
            _paths,
            settings,
            bridge,
            new RestoreTarget(Path.Combine(_paths.DataDir, "NadekoBot.db")));

        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    private void Authorize()
        => _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);

    private const string COMPUTED = "computed";

    private static HttpRequestMessage UpdateRequest(
        byte[] package,
        string? sha256 = COMPUTED,
        string? version = VERSION,
        string contentType = "application/gzip")
    {
        var content = new ByteArrayContent(package);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return UpdateRequest(content, sha256 == COMPUTED ? Sha256(package) : sha256, version);
    }

    private static HttpRequestMessage UpdateRequest(HttpContent content, string? sha256, string? version = VERSION)
    {
        content.Headers.ContentType ??= new MediaTypeHeaderValue("application/gzip");
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/update") { Content = content };
        if (sha256 is not null)
            req.Headers.Add("X-Content-Sha256", sha256);
        if (version is not null)
            req.Headers.Add("X-Bot-Version", version);

        return req;
    }

    private static string Sha256(byte[] data)
        => Convert.ToHexStringLower(SHA256.HashData(data));

    private static async Task AssertErrorAsync(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.That(res.StatusCode, Is.EqualTo(status));
        var body = JsonSerializer.Deserialize(await res.Content.ReadAsStringAsync(), ApiJsonContext.Default.ErrorDto);
        Assert.That(body?.Error, Is.EqualTo(code));
    }

    private static TarEntry[] ValidEntries()
    {
        var tool = File($"{TOP}/tool.sh", "#!/bin/sh\n");
        tool.Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                    | UnixFileMode.SetUser;

        return
        [
            Dir(TOP),
            File($"{TOP}/NadekoBot.Hosted", VERSION_SCRIPT),
            File($"{TOP}/NadekoBot.dll", "dll"),
            Dir($"{TOP}/strings"),
            File($"{TOP}/strings/en-US.yml", "hi"),
            tool,
            Link(TarEntryType.SymbolicLink, $"{TOP}/link.dll", "NadekoBot.dll"),
            Link(TarEntryType.HardLink, $"{TOP}/hard.dll", $"{TOP}/NadekoBot.dll")
        ];
    }

    private static TarEntry Dir(string name)
        => new PaxTarEntry(TarEntryType.Directory, name + "/")
        {
            Mode = FILE_MODE | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute
        };

    // Mode 0644 even for the executable: the package must make NadekoBot.Hosted runnable itself.
    private static TarEntry File(string name, string content)
        => new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            Mode = FILE_MODE,
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
        };

    private static TarEntry Link(TarEntryType type, string name, string target)
        => new PaxTarEntry(type, name) { LinkName = target };

    private static byte[] Package(System.Collections.Generic.IEnumerable<TarEntry> entries)
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var entry in entries)
                writer.WriteEntry(entry);
        }

        return Gzip(tar.ToArray());
    }

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gz.Write(data);

        return ms.ToArray();
    }

    private static ByteArrayContent ZipContent()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = archive.CreateEntry("NadekoBot.db").Open();
            w.Write(BackupArchive.SqliteHeader);
        }

        var body = new ByteArrayContent(ms.ToArray());
        body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return body;
    }

    // Generates zeros on the fly, so a test can send more than the limit without holding it in memory.
    private sealed class LazyContent(long length, bool knownLength) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[81920];
            for (var left = length; left > 0; left -= buffer.Length)
                await stream.WriteAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)));
        }

        protected override bool TryComputeLength(out long size)
        {
            size = length;
            return knownLength;
        }
    }
}
