#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NadekoBot.Common;
using NadekoBot.Common.Yml;
using NadekoBot.Db.Models;
using NadekoBot.Hosted;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Services;
using NadekoBot.Tests.Waifu;
using NUnit.Framework;
using Serilog;

namespace NadekoBot.Tests;

// Real Kestrel on a free port, so the tests see what a manager sees on the wire.
[TestFixture]
public sealed class ApiHttpTests
{
    private const string API_KEY = "placeholder-key-xxxxxxxxxxxxxxxx";
    private const string OWNER_A = "105635576866156544";
    private const string OWNER_B = "145521851676884992";

    private string _dir = null!;
    private HostPaths _paths = null!;
    private WebApplication? _app;
    private HttpClient? _http;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-http-" + Guid.NewGuid().ToString("N"));
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
    public void Health_ReportsEachState()
    {
        var time = new FakeTimeProvider();
        var bridge = new BotBridge(time);

        Assert.That(bridge.Health(hasApiKey: false).Status, Is.EqualTo(HealthStatus.UNCONFIGURED));

        bridge.SetPhase(BotPhase.NoToken);
        Assert.That(bridge.Health(true).Status, Is.EqualTo(HealthStatus.NO_TOKEN));
        bridge.SetPhase(BotPhase.InvalidToken);
        Assert.That(bridge.Health(true).Status, Is.EqualTo(HealthStatus.INVALID_TOKEN));
        bridge.SetPhase(BotPhase.Starting);
        Assert.That(bridge.Health(true), Is.EqualTo(new BotHealth(HealthStatus.STARTING, null)));
        bridge.SetFailed(FailureReason.DISALLOWED_INTENTS);
        Assert.That(bridge.Health(true), Is.EqualTo(new BotHealth(HealthStatus.FAILED, FailureReason.DISALLOWED_INTENTS)));

        // A gateway close which lands before startup finishes keeps its status.
        Assert.That(bridge.SetReady(new ServiceCollection().BuildServiceProvider(), null), Is.False);
        Assert.That(bridge.Health(true).Status, Is.EqualTo(HealthStatus.FAILED));

        bridge.SetPhase(BotPhase.Starting);
        Assert.That(bridge.SetReady(new ServiceCollection().BuildServiceProvider(), null), Is.True);
        Assert.That(bridge.Health(true), Is.EqualTo(new BotHealth(HealthStatus.OK, null)));

        bridge.MarkDisconnected();
        time.Advance(BotBridge.DisconnectGrace - TimeSpan.FromSeconds(1));
        Assert.That(bridge.Health(true).Status, Is.EqualTo(HealthStatus.OK));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(bridge.Health(true).Status, Is.EqualTo(HealthStatus.DISCONNECTED));

        bridge.MarkConnected();
        Assert.That(bridge.Health(true).Status, Is.EqualTo(HealthStatus.OK));
    }

    [Test]
    public async Task Health_OmitsTheReasonUnlessFailedAndReportsTheLastUpdateResult()
    {
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.Starting);
        await StartAsync(API_KEY, bridge);

        var healthy = await _http!.GetStringAsync("/api/health");
        Assert.That(healthy, Does.Not.Contain("reason"));
        using (var none = JsonDocument.Parse(healthy))
            Assert.That(none.RootElement.GetProperty("update").ValueKind, Is.EqualTo(JsonValueKind.Null));

        bridge.SetFailed(FailureReason.STARTUP_FAILED);
        var failedStart = JsonSerializer.Deserialize(await _http.GetStringAsync("/api/health"), ApiJsonContext.Default.HealthDto);
        Assert.That(failedStart,
            Is.EqualTo(new HealthDto(HealthStatus.FAILED, 0, 1, StatsService.BotVersion, FailureReason.STARTUP_FAILED)));

        bridge.SetPhase(BotPhase.NoToken);
        File.WriteAllText(_paths.UpdateResult, "rollback_failed:7.6.1:no_previous_install\n");
        await RestartAsync(API_KEY, bridge);
        using (var failed = JsonDocument.Parse(await _http!.GetStringAsync("/api/health")))
        {
            Assert.That(failed.RootElement.GetProperty("update").GetRawText(),
                Is.EqualTo("""{"result":"rollback_failed","version":"7.6.1","reason":"no_previous_install"}"""));
            Assert.That(failed.RootElement.TryGetProperty("reason", out _), Is.False);
        }

        // The file is read at startup only: the apply step writes it before the bot starts.
        File.WriteAllText(_paths.UpdateResult, "applied:7.6.2\n");
        using (var cached = JsonDocument.Parse(await _http.GetStringAsync("/api/health")))
            Assert.That(cached.RootElement.GetProperty("update").GetProperty("version").GetString(), Is.EqualTo("7.6.1"));

        await RestartAsync(API_KEY, bridge);
        using (var applied = JsonDocument.Parse(await _http!.GetStringAsync("/api/health")))
        {
            Assert.That(applied.RootElement.GetProperty("update").GetRawText(),
                Is.EqualTo("""{"result":"applied","version":"7.6.2","reason":null}"""));
        }
    }

    [Test]
    public async Task Creds_FirstUseSetsTheKeyAndOwnersAndKeepsTheRest()
    {
        File.WriteAllText(_paths.Creds, Yaml.Serializer.Serialize(new Creds { Token = "tok" }));
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.Starting);
        await StartAsync(string.Empty, bridge);

        Assert.That(await HealthAsync(), Is.EqualTo(HealthStatus.UNCONFIGURED));

        using (var shortKey = await _http!.PutAsync("/api/creds", Json(new { apiKey = "short" })))
            await AssertErrorAsync(shortKey, HttpStatusCode.BadRequest, "invalid_value");

        using (var noOwners = await _http.PutAsync("/api/creds", Json(new { apiKey = API_KEY, ownerIds = Array.Empty<string>() })))
            await AssertErrorAsync(noOwners, HttpStatusCode.BadRequest, "invalid_value");

        Assert.That(await HealthAsync(), Is.EqualTo(HealthStatus.UNCONFIGURED));

        using (var ok = await _http.PutAsync("/api/creds", Json(new { apiKey = API_KEY, ownerIds = new[] { OWNER_A } })))
            Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        Assert.That(CredsFile.Read(_paths.Creds)!.OwnerIds, Is.EqualTo(new[] { ulong.Parse(OWNER_A) }));

        // The key applies at once, so a second caller which raced the first one cannot replace it.
        using (var raced = await _http.PutAsync("/api/creds", Json(new { apiKey = new string('z', 40) })))
            await AssertErrorAsync(raced, HttpStatusCode.Unauthorized, "unauthorized");

        Assert.That(ApiSettingsFile.LoadOrCreate(_paths.ApiSettings).ApiKey, Is.EqualTo(API_KEY));
        Assert.That(CredsFile.Read(_paths.Creds)!.Token, Is.EqualTo("tok"));

        await RestartAsync(API_KEY, bridge);

        using (var noBearer = await _http!.PutAsync("/api/creds", Json(new { apiKey = API_KEY, token = "" })))
            await AssertErrorAsync(noBearer, HttpStatusCode.Unauthorized, "unauthorized");

        Authorize();
        using (var clear = await _http.PutAsync("/api/creds", Json(new { apiKey = API_KEY, token = "" })))
            Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        Assert.That(CredsFile.Read(_paths.Creds)!.Token, Is.Empty);
        Assert.That(CredsFile.Read(_paths.Creds)!.OwnerIds, Is.EqualTo(new[] { ulong.Parse(OWNER_A) }));
    }

    [Test]
    public async Task Owners_ReplaceTheListWithoutARestart()
    {
        File.WriteAllText(_paths.Creds, Yaml.Serializer.Serialize(new Creds { Token = "tok", OwnerIds = [ulong.Parse(OWNER_A)] }));
        var creds = new FileCredsProvider(_paths.Creds);
        using var db = new TestDbService();
        await using (var ctx = db.GetDbContext())
            await ctx.GetTable<DiscordUser>().InsertAsync(() => new DiscordUser { UserId = ulong.Parse(OWNER_B), Username = "stored" });

        using var client = new DiscordSocketClient();
        var services = new ServiceCollection()
            .AddSingleton<IBotCredsProvider>(creds)
            .AddSingleton<DbService>(db)
            .AddSingleton(client)
            .BuildServiceProvider();
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.Starting);
        bridge.SetReady(services, null);
        await StartAsync(API_KEY, bridge);
        Authorize();

        Assert.That(await _http!.GetStringAsync("/api/owners"), Is.EqualTo($$"""{"owners":[{"id":"{{OWNER_A}}"}]}"""));

        object[] refused =
        [
            new { ids = Array.Empty<string>() },
            new { },
            new { ids = new[] { "123" } },
            new { ids = new[] { "0" + OWNER_A[1..] } },
            new { ids = new[] { OWNER_A + "x" } },
            new { ids = Enumerable.Range(0, OwnerIdList.MAX_OWNERS + 1).Select(i => (ulong.Parse(OWNER_A) + (ulong)i).ToString()).ToArray() }
        ];
        foreach (var body in refused)
        {
            using var res = await _http.PutAsync("/api/owners", Json(body));
            await AssertErrorAsync(res, HttpStatusCode.BadRequest, "invalid_value");
        }

        Assert.That(CredsFile.Read(_paths.Creds)!.OwnerIds, Is.EqualTo(new[] { ulong.Parse(OWNER_A) }));

        using var ok = await _http.PutAsync("/api/owners", Json(new { ids = new[] { OWNER_B, OWNER_A, OWNER_B } }));
        Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await ok.Content.ReadAsStringAsync(),
            Is.EqualTo($$"""{"owners":[{"id":"{{OWNER_B}}","name":"stored"},{"id":"{{OWNER_A}}"}]}"""));

        var expected = new[] { ulong.Parse(OWNER_B), ulong.Parse(OWNER_A) };
        Assert.That(creds.GetCreds().OwnerIds, Is.EqualTo(expected));
        Assert.That(CredsFile.Read(_paths.Creds)!.OwnerIds, Is.EqualTo(expected));
        Assert.That(CredsFile.Read(_paths.Creds)!.Token, Is.EqualTo("tok"));
    }

    [Test]
    public async Task Logs_StreamTheTailThenNewLinesWhileTheBotIsDown()
    {
        using var logs = new LogStreamService(TimeProvider.System);
        logs.AddSecrets(null, API_KEY);
        using var logger = new LoggerConfiguration().WriteTo.Sink(logs.Sink).CreateLogger();
        foreach (var line in new[] { "one", "two", "three" })
            logger.Information("line {Name}", line);

        await ApiLogStreamTests.WaitForLinesAsync(logs, 3);

        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.NoToken);
        await StartAsync(API_KEY, bridge, logs);
        Authorize();

        using (var negative = await _http!.GetAsync("/api/logs?tail=-1"))
            await AssertErrorAsync(negative, HttpStatusCode.BadRequest, "invalid_value");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var res = await _http.GetAsync("/api/logs?tail=2", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(res.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        Assert.That(res.Headers.CacheControl?.NoStore, Is.True);

        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(cts.Token));
        Assert.That(await ReadLogTextAsync(reader, cts.Token), Is.EqualTo("line two"));
        Assert.That(await ReadLogTextAsync(reader, cts.Token), Is.EqualTo("line three"));

        logger.Warning("line {Key}", API_KEY);
        Assert.That(await ReadLogTextAsync(reader, cts.Token), Is.EqualTo("line " + LogStreamService.REDACTED));
    }

    private static async Task<string?> ReadLogTextAsync(StreamReader reader, CancellationToken ct)
    {
        var data = await reader.ReadLineAsync(ct);
        Assert.That(data, Does.StartWith("data: "));
        Assert.That(await reader.ReadLineAsync(ct), Is.Empty);
        var line = JsonSerializer.Deserialize(data!["data: ".Length..], ApiJsonContext.Default.LogLineDto)!;
        return line.Text;
    }

    [Test]
    public async Task AiKey_IsWriteOnlyAndAppliesWithoutARestart()
    {
        File.WriteAllText(_paths.Creds, Yaml.Serializer.Serialize(new Creds { Token = "tok", Gpt3ApiKey = "old" }));
        var creds = new FileCredsProvider(_paths.Creds);
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.Starting);
        bridge.SetReady(new ServiceCollection().AddSingleton<IBotCredsProvider>(creds).BuildServiceProvider(), null);
        await StartAsync(API_KEY, bridge);
        Authorize();

        using (var bad = await _http!.PutAsync("/api/ai-key", Json(new { key = "has space" })))
            await AssertErrorAsync(bad, HttpStatusCode.BadRequest, "invalid_value");

        using (var set = await _http.PutAsync("/api/ai-key", Json(new { key = "sk-test" })))
            Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var status = await _http.GetStringAsync("/api/ai-key");
        Assert.That(status, Is.EqualTo("""{"set":true}"""));
        Assert.That(creds.GetCreds().AiApiKey, Is.EqualTo("sk-test"));
        Assert.That(CredsFile.Read(_paths.Creds)!.Token, Is.EqualTo("tok"));
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(_paths.Creds), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));

        using (var clear = await _http.DeleteAsync("/api/ai-key"))
            Assert.That(clear.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That(await _http.GetStringAsync("/api/ai-key"), Is.EqualTo("""{"set":false}"""));
        Assert.That(CredsFile.Read(_paths.Creds)!.Gpt3ApiKey, Is.Empty);
    }

    [Test]
    public async Task SystemDisk_ReportsTheVolumeBehindTheDataFolder()
    {
        await StartAsync(API_KEY, new BotBridge());

        using (var anonymous = await _http!.GetAsync("/api/system/disk"))
            await AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, "unauthorized");

        Authorize();
        using (var json = JsonDocument.Parse(await _http.GetStringAsync("/api/system/disk")))
        {
            var root = json.RootElement;
            Assert.That(root.EnumerateObject().Select(static p => p.Name), Is.EqualTo(new[] { "usedBytes", "totalBytes" }));
            var used = root.GetProperty("usedBytes").GetInt64();
            var total = root.GetProperty("totalBytes").GetInt64();
            Assert.That(total, Is.GreaterThan(0));
            Assert.That(used, Is.InRange(0, total));
        }

        // A symlinked data folder is measured where it points, as the container's data volume is.
        var linked = Path.Combine(_dir, "linked");
        Directory.CreateSymbolicLink(linked, _paths.DataDir);
        Assert.That(ManagedEndpoints.ReadDiskUsage(linked)?.TotalBytes,
            Is.EqualTo(ManagedEndpoints.ReadDiskUsage(_paths.DataDir)?.TotalBytes));

        Assert.That(ManagedEndpoints.ReadDiskUsage(Path.Combine(_dir, "missing")), Is.Null);
    }

    [Test]
    public async Task FrameworkErrors_HaveErrorBodies()
    {
        await StartAsync(API_KEY, new BotBridge());

        // Without the key, a wrong method or an unknown path must not tell which routes exist.
        using (var probe = await _http!.PostAsync("/api/status", null))
            await AssertErrorAsync(probe, HttpStatusCode.Unauthorized, "unauthorized");
        using (var probe = await _http.GetAsync("/api/nope"))
            await AssertErrorAsync(probe, HttpStatusCode.Unauthorized, "unauthorized");

        Authorize();

        using (var notFound = await _http.GetAsync("/api/nope"))
            await AssertErrorAsync(notFound, HttpStatusCode.NotFound, "not_found");

        using (var badJson = await _http.PutAsync("/api/creds",
                   new StringContent("{", Encoding.UTF8, "application/json")))
            await AssertErrorAsync(badJson, HttpStatusCode.BadRequest, "bad_request");

        using (var wrongType = await _http.PutAsync("/api/creds", new StringContent("x", Encoding.UTF8, "text/plain")))
            await AssertErrorAsync(wrongType, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");

        using (var tooBig = await _http.PutAsync("/api/creds",
                   new StringContent(new string('x', ApiLimits.MAX_REQUEST_BODY_BYTES + 1), Encoding.UTF8, "application/json")))
            await AssertErrorAsync(tooBig, HttpStatusCode.RequestEntityTooLarge, "too_large");

        using (var missingField = await _http.PutAsync("/api/creds", Json(new { token = "x" })))
            await AssertErrorAsync(missingField, HttpStatusCode.BadRequest, "bad_request");

        using (var notZip = await _http.PostAsync("/api/backups/import",
                   new StringContent("PK", Encoding.UTF8, "text/plain")))
            await AssertErrorAsync(notZip, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");

        using (var paddedId = await _http.GetAsync("/api/guilds/%201/roles"))
            await AssertErrorAsync(paddedId, HttpStatusCode.NotFound, "not_found");

        using (var wrongMethod = await _http.PostAsync("/api/health", null))
            await AssertErrorAsync(wrongMethod, HttpStatusCode.MethodNotAllowed, "method_not_allowed");

        using (var notReady = await _http.GetAsync("/api/status"))
            await AssertErrorAsync(notReady, HttpStatusCode.ServiceUnavailable, "not_ready");
    }

    [Test]
    public async Task Feed_SendsKeepAlivesToOpenStreams()
    {
        var time = new FakeTimeProvider();
        using var feed = new CommandFeedService(await FakeCommandHandler.CreateAsync(), time);
        using var sub = feed.Subscribe()!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // The keepalive loop starts its timer on another thread, so advance until a tick lands.
        var read = sub.Reader.ReadAsync(cts.Token).AsTask();
        while (!read.IsCompleted)
        {
            time.Advance(CommandFeedService.KeepAliveInterval);
            await Task.Delay(10, cts.Token);
        }

        Assert.That((await read).IsKeepAlive, Is.True);
    }

    [Test]
    public async Task Feed_StreamsUnnamedFramesWithoutASinceQuery()
    {
        var handler = await FakeCommandHandler.CreateAsync();
        using var feed = new CommandFeedService(handler, TimeProvider.System);
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.Starting);
        bridge.SetReady(new ServiceCollection().BuildServiceProvider(), feed);
        await StartAsync(API_KEY, bridge);
        Authorize();

        await handler.RaiseErrorAsync("boom");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var res = await _http!.GetAsync("/api/feed", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(res.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));

        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(cts.Token));
        var id = await reader.ReadLineAsync(cts.Token);
        var data = await reader.ReadLineAsync(cts.Token);

        Assert.That(id, Does.StartWith("id: "));
        Assert.That(data, Does.StartWith("data: "));
        Assert.That(await reader.ReadLineAsync(cts.Token), Is.Empty);
        using var json = JsonDocument.Parse(data!["data: ".Length..]);
        Assert.That(json.RootElement.GetProperty("type").GetString(), Is.EqualTo("error"));
        Assert.That(json.RootElement.GetProperty("error").GetString(), Is.EqualTo("boom"));
        Assert.That(json.RootElement.GetProperty("id").GetInt64(), Is.EqualTo(long.Parse(id!["id: ".Length..])));

        var held = new List<FeedSubscription>();
        while (feed.Subscribe() is { } extra)
            held.Add(extra);

        Assert.That(held, Has.Count.EqualTo(CommandFeedService.MAX_SUBSCRIBERS - 1));
        using (var refused = await _http.GetAsync("/api/feed", cts.Token))
            await AssertErrorAsync(refused, HttpStatusCode.TooManyRequests, "too_many_streams");

        foreach (var sub in held)
            sub.Dispose();
    }

    [Test]
    public async Task Backups_CreateAndImportNeverOverwriteAnArchive()
    {
        using var db = new TestDbService();
        var services = new ServiceCollection().AddSingleton<DbService>(db).BuildServiceProvider();
        var bridge = new BotBridge();
        bridge.SetPhase(BotPhase.Starting);
        bridge.SetReady(services, null);
        File.WriteAllText(Path.Combine(_paths.DataDir, "bot.yml"), "prefix: '.'");
        await StartAsync(API_KEY, bridge);
        Authorize();

        var taken = FillBackupNames();

        using (var created = await _http!.PostAsync("/api/backup", null))
            await AssertErrorAsync(created, HttpStatusCode.Conflict, "name_taken");

        using (var imported = await _http.PostAsync("/api/backups/import", Zip(("NadekoBot.db", BackupArchive.SqliteHeader.ToArray()))))
            await AssertErrorAsync(imported, HttpStatusCode.Conflict, "name_taken");

        Assert.That(taken.Select(File.ReadAllText), Is.All.EqualTo("keep"));
        Assert.That(Directory.GetFiles(_paths.BackupDir).Length, Is.EqualTo(taken.Length));

        foreach (var path in taken)
            File.Delete(path);

        using var ok = await _http.PostAsync("/api/backup", null);
        Assert.That(ok.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dto = JsonSerializer.Deserialize(await ok.Content.ReadAsStringAsync(), ApiJsonContext.Default.BackupDto)!;
        var archive = Path.Combine(_paths.BackupDir, dto.FileName);

        Assert.That(BackupArchive.ValidateFile(archive, BackupLimits.Restore, out _), Is.Null);
        using (var zip = ZipFile.OpenRead(archive))
            Assert.That(zip.Entries.Select(static e => e.FullName), Is.EquivalentTo(new[] { "NadekoBot.db", "data/bot.yml" }));

        using var list = await _http.GetAsync("/api/backups");
        var listed = JsonSerializer.Deserialize(await list.Content.ReadAsStringAsync(),
            ApiJsonContext.Default.BackupDtoArray)!;
        Assert.That(listed.Select(static b => b.FileName), Is.EqualTo(new[] { dto.FileName }));
    }

    private string[] FillBackupNames()
    {
        Directory.CreateDirectory(_paths.BackupDir);
        var now = DateTime.UtcNow;
        var names = new string[10];
        for (var i = 0; i < names.Length; i++)
        {
            names[i] = Path.Combine(_paths.BackupDir, $"nadeko-backup-{now.AddSeconds(i - 2):yyyyMMdd-HHmmss}.zip");
            File.WriteAllText(names[i], "keep");
        }

        return names;
    }

    private async Task StartAsync(string apiKey, BotBridge bridge, LogStreamService? logs = null)
    {
        var settings = new ApiSettings { Port = 0 }.WithKey(apiKey);
        _app = ApiHost.Build(
            new HostContext(HostMode.Standalone, 0, 1),
            _paths,
            settings,
            bridge,
            new RestoreTarget(Path.Combine(_paths.DataDir, "NadekoBot.db")),
            logs);

        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    private async Task RestartAsync(string apiKey, BotBridge bridge)
    {
        _http?.Dispose();
        await _app!.DisposeAsync();
        await StartAsync(apiKey, bridge);
    }

    private void Authorize()
        => _http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);

    private async Task<string?> HealthAsync()
    {
        var dto = JsonSerializer.Deserialize(await _http!.GetStringAsync("/api/health"), ApiJsonContext.Default.HealthDto);
        return dto?.Status;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.That(res.StatusCode, Is.EqualTo(status));
        var body = JsonSerializer.Deserialize(await res.Content.ReadAsStringAsync(), ApiJsonContext.Default.ErrorDto);
        Assert.That(body?.Error, Is.EqualTo(code));
    }

    private static StringContent Json<T>(T value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static ByteArrayContent Zip(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var w = archive.CreateEntry(name).Open();
                w.Write(content);
            }
        }

        var body = new ByteArrayContent(ms.ToArray());
        body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return body;
    }

    private sealed class FileCredsProvider(string path) : IBotCredsProvider
    {
        private Creds _creds = CredsFile.Read(path)!;

        public void Reload()
            => _creds = CredsFile.Read(path)!;

        public IBotCreds GetCreds()
            => _creds;

        public void ModifyCredsFile(Action<IBotCreds> func)
            => throw new NotSupportedException();
    }

    private sealed class FakeCommandHandler : ICommandHandler
    {
        private readonly CommandInfo _cmd;

        private FakeCommandHandler(CommandInfo cmd)
            => _cmd = cmd;

        public static async Task<FakeCommandHandler> CreateAsync()
        {
            var module = await new CommandService()
                .CreateModuleAsync(string.Empty, static b => b
                    .WithName("Utility")
                    .AddCommand("ping", static (_, _, _, _) => Task.CompletedTask, static _ => { }));
            return new(module.Commands[0]);
        }

        public event Func<IUserMessage, CommandInfo, Task> CommandExecuted = static (_, _) => Task.CompletedTask;
        public event Func<CommandInfo, ITextChannel?, string, Task> CommandErrored = static (_, _, _) => Task.CompletedTask;

        public Task RaiseErrorAsync(string error)
            => CommandErrored(_cmd, null, error);

        public string GetPrefix(IGuild? ctxGuild)
            => ".";

        public string GetPrefix(ulong? id = null)
            => ".";

        public string SetDefaultPrefix(string toSet)
            => toSet;

        public string SetPrefix(IGuild ctxGuild, string toSet)
            => toSet;

        public Task TryRunCommand(SocketGuild? guild, ISocketMessageChannel channel, IUserMessage usrMsg)
            => Task.CompletedTask;
    }
}
