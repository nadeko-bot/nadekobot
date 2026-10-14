#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NadekoBot.Common;
using NadekoBot.Common.Yml;
using NadekoBot.Hosted;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class ApiContractTests
{
    // The NadeKloud contract (ndkloud docs/nadekobot-managed-mode.md). A change here is a change for the dashboard.
    private static readonly string[] _standaloneRoutes =
    [
        "GET /api/health",
        "PUT /api/creds",
        "POST /api/system/restart",
        "GET /api/system/disk",
        "GET /api/logs",
        "PUT /api/update",
        "GET /api/backups",
        "GET /api/backups/{fileName}",
        "DELETE /api/backups/{fileName}",
        "POST /api/backups/import",
        "POST /api/backups/{fileName}/restore",
        "GET /api/status",
        "GET /api/guilds",
        "GET /api/configs",
        "GET /api/configs/{name}",
        "PUT /api/configs/{name}/{prop}",
        "GET /api/expressions",
        "GET /api/expressions/export",
        "POST /api/expressions",
        "PUT /api/expressions/{id:int}",
        "DELETE /api/expressions/{id:int}",
        "GET /api/feed",
        "POST /api/backup",
        "GET /api/ai-key",
        "PUT /api/ai-key",
        "DELETE /api/ai-key",
        "GET /api/owners",
        "PUT /api/owners",
        "GET /api/guilds/{guildId}",
        "GET /api/guilds/{guildId}/channels",
        "GET /api/guilds/{guildId}/roles",
        "GET /api/guilds/{guildId}/greets",
        "PUT /api/guilds/{guildId}/greets/{type}",
        "POST /api/guilds/{guildId}/greets/{type}/test",
        "GET /api/guilds/{guildId}/expressions",
        "GET /api/guilds/{guildId}/expressions/export",
        "POST /api/guilds/{guildId}/expressions",
        "PUT /api/guilds/{guildId}/expressions/{id:int}",
        "DELETE /api/guilds/{guildId}/expressions/{id:int}",
        "DELETE /api/guilds/{guildId}/expressions",
        "GET /api/guilds/{guildId}/quotes",
        "GET /api/guilds/{guildId}/quotes/search",
        "POST /api/guilds/{guildId}/quotes",
        "PUT /api/guilds/{guildId}/quotes/{id:int}",
        "DELETE /api/guilds/{guildId}/quotes/{id:int}",
        "DELETE /api/guilds/{guildId}/quotes",
        "GET /api/guilds/{guildId}/warnings",
        "POST /api/guilds/{guildId}/warnings/{id:int}/forgive",
        "DELETE /api/guilds/{guildId}/warnings/{id:int}",
        "GET /api/guilds/{guildId}/warn-punishments",
        "PUT /api/guilds/{guildId}/warn-punishments/{count:int}",
        "DELETE /api/guilds/{guildId}/warn-punishments/{count:int}",
        "GET /api/guilds/{guildId}/warn-expiry",
        "PUT /api/guilds/{guildId}/warn-expiry",
        "GET /api/guilds/{guildId}/xp/leaderboard",
        "GET /api/guilds/{guildId}/xp/rewards",
        "PUT /api/guilds/{guildId}/xp/rewards/roles/{level:int}",
        "DELETE /api/guilds/{guildId}/xp/rewards/roles/{level:int}",
        "PUT /api/guilds/{guildId}/xp/rewards/currency/{level:int}",
        "DELETE /api/guilds/{guildId}/xp/rewards/currency/{level:int}",
        "POST /api/guilds/{guildId}/import",
        "POST /api/guilds/{guildId}/xp/users/{userId}/add",
        "PUT /api/guilds/{guildId}/xp/users/{userId}/level",
        "DELETE /api/guilds/{guildId}/xp/users/{userId}",
        "DELETE /api/guilds/{guildId}/xp",
        "GET /api/guilds/{guildId}/health",
        "GET /api/guilds/{guildId}/activity"
    ];

    private const string DB_ENV = "bot_Db__ConnectionString";

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nadeko-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
        => Directory.Delete(_dir, recursive: true);

    [Test]
    public async Task Routes_MatchTheContractWithOptionalPageAndSinceAndEveryHandlerServiceIsRegistered()
    {
        await using var app = Build(HostMode.Standalone);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var routes = endpoints
            .Select(static e => $"{e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods[0]} "
                                + e.RoutePattern.RawText!.TrimEnd('/'))
            .ToArray();
        Assert.That(routes, Is.EquivalentTo(_standaloneRoutes));

        var isService = app.Services.GetRequiredService<IServiceProviderIsService>();
        var missing = endpoints
            .SelectMany(static e => e.Metadata.GetMetadata<MethodInfo>()!.GetParameters())
            .Select(static p => p.ParameterType)
            .Where(t => IsInjected(t) && !isService.IsService(t))
            .Distinct()
            .ToArray();
        Assert.That(missing, Is.Empty, "Register these with AddBotService<T> or AddSingleton in ApiHost.Build");

        var pageAndSince = endpoints
            .SelectMany(static e => e.Metadata.GetOrderedMetadata<IParameterBindingMetadata>())
            .Where(static p => p.Name is "page" or "since")
            .ToArray();
        Assert.That(pageAndSince, Is.Not.Empty);
        Assert.That(pageAndSince.Select(static p => p.IsOptional), Is.All.True);
    }

    // A coordinated shard serves server dashboards; a currency reward creates currency, so only the owner sets it.
    [Test]
    public async Task CoordinatedRoutes_AreTheGuildRoutesWithoutTheOwnerGuildRoutes()
    {
        await using var app = Build(HostMode.Coordinated);
        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static e => $"{e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods[0]} "
                                + e.RoutePattern.RawText!.TrimEnd('/'))
            .ToArray();

        var expected = _standaloneRoutes
            .Where(static r => r == "GET /api/health"
                               || (r.Contains("/api/guilds/{guildId}", StringComparison.Ordinal)
                                   && !r.Contains("/xp/rewards/currency/", StringComparison.Ordinal)
                                   && !r.EndsWith("/import", StringComparison.Ordinal)
                                   && !r.EndsWith("/activity", StringComparison.Ordinal)))
            .ToArray();
        Assert.That(routes, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task Gate_AnswersNotFoundForAGuildTheBotIsNotIn()
    {
        var bridge = new BotBridge();
        bridge.AttachClient(new DiscordSocketClient());
        bridge.SetPhase(BotPhase.Starting);
        bridge.SetReady(new ServiceCollection().BuildServiceProvider(), null);
        var reached = false;
        var gate = new ApiGateMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            new ApiKey(string.Empty),
            bridge,
            new HostContext(HostMode.Standalone, 0, 1));

        foreach (var guildId in new[] { "123", "not-a-number" })
        {
            var http = new DefaultHttpContext();
            http.SetEndpoint(new Endpoint(null,
                new EndpointMetadataCollection(new ApiAccessMetadata(ApiAccess.Bot | ApiAccess.Guild)),
                "test"));
            http.Request.RouteValues[ApiGateMiddleware.GUILD_ID_ROUTE_VALUE] = guildId;

            await gate.InvokeAsync(http);

            Assert.That(http.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        }

        Assert.That(reached, Is.False);
    }

    [Test]
    public void ApiSettings_RejectUnsafeOrBrokenValuesAndKeysWhichAreNotToken68()
    {
        Assert.That(new ApiSettings().Validate(0), Is.Null);
        Assert.That(new ApiSettings { Address = "::1" }.Validate(0), Is.Null);
        Assert.That(new ApiSettings { Address = "localhost" }.Validate(0), Is.Not.Null);
        Assert.That(new ApiSettings { Port = 0 }.Validate(0), Is.Not.Null);
        Assert.That(new ApiSettings { Port = 65530 }.Validate(31), Is.Not.Null);
        Assert.That(new ApiSettings { MaxImportMb = 0 }.Validate(0), Is.Not.Null);
        Assert.That(new ApiSettings { MaxUpdateMb = ApiSettings.MIN_UPDATE_MB - 1 }.Validate(0), Is.Not.Null);
        Assert.That(new ApiSettings { MaxUpdateMb = ApiSettings.MAX_UPDATE_MB + 1 }.Validate(0), Is.Not.Null);
        Assert.That(new ApiSettings { MaxUpdateMb = ApiSettings.MAX_UPDATE_MB }.Validate(0), Is.Null);
        Assert.That(new ApiSettings { ApiKey = "short" }.Validate(0), Is.Not.Null);
        Assert.That(new ApiSettings { ApiKey = "   " }.Validate(0), Is.Not.Null);

        var empty = Path.Combine(_dir, "empty.yml");
        File.WriteAllText(empty, string.Empty);
        var broken = Path.Combine(_dir, "broken.yml");
        File.WriteAllText(broken, "port: [");
        var misspelled = Path.Combine(_dir, "misspelled.yml");
        File.WriteAllText(misspelled, "apikey: placeholder-key-xxxxxxxxxxxxxxxx");
        var blank = Path.Combine(_dir, "blank.yml");
        File.WriteAllText(blank, "apiKey:");

        Assert.Throws<InvalidDataException>(() => ApiSettingsFile.LoadOrCreate(empty));
        Assert.Throws<InvalidDataException>(() => ApiSettingsFile.LoadOrCreate(broken));
        Assert.Throws<InvalidDataException>(() => ApiSettingsFile.LoadOrCreate(misspelled));
        Assert.That(ApiSettingsFile.LoadOrCreate(blank).HasKey, Is.False);

        string[] valid = ["placeholder-key-xxxxxxxxxxxxxxxx", "abcdefghijklmnopqrstuvwxyz0123-._~+/=="];
        string[] invalid =
        [
            "abcdefghijklmnopqrstuvwxyz01234", "abcdefghijklmnopqrstuvwxyz01234 5",
            "abcdefghijklmnop=rstuvwxyz0123456", "placeholder-key-xxxxxxxxxxxxxxxx\n"
        ];

        Assert.That(valid.Where(static k => ApiSettings.IsValidKey(k)), Is.EqualTo(valid));
        Assert.That(invalid.Where(static k => ApiSettings.IsValidKey(k)), Is.Empty);
    }

    [Test]
    public void CredsFile_ReplacesAnUnreadableFileKeepsTheRestOfCredsAndSeesTheEnvironmentOverrides()
    {
        var path = Path.Combine(_dir, "creds.yml");
        File.WriteAllText(path, "token: [");

        Assert.That(CredsFile.TryRead(path, out var creds), Is.False);
        Assert.That(creds, Is.Null);
        Assert.That(CredsFile.IsValidToken("abc.def"), Is.True);
        Assert.That(CredsFile.IsValidToken("abc def"), Is.False);

        Assert.That(CredsFile.LoadEffective(path).Token, Is.Null.Or.Empty);

        CredsFile.WriteToken(path, "abc.def");

        Assert.That(CredsFile.Read(path)!.Token, Is.EqualTo("abc.def"));
        Assert.That(File.ReadAllText(path + ".broken"), Is.EqualTo("token: ["));

        var kept = new Creds { Token = "old" };
        kept.OwnerIds.Add(42);
        File.WriteAllText(path, Yaml.Serializer.Serialize(kept));

        CredsFile.WriteToken(path, "new");
        var read = CredsFile.Read(path)!;

        Assert.That(read.Token, Is.EqualTo("new"));
        Assert.That(read.OwnerIds, Is.EquivalentTo(new ulong[] { 42 }));
        Assert.That(CredsFile.SqliteDbPath(read, _dir), Is.EqualTo(Path.Combine(_dir, "data", "NadekoBot.db")));

        read.Db.Type = "postgresql";
        Assert.That(CredsFile.SqliteDbPath(read, _dir), Is.Null);

        CredsFile.WriteToken(path, "from.file");
        Environment.SetEnvironmentVariable(DB_ENV, "Data Source=data/other.db");
        try
        {
            var effective = CredsFile.LoadEffective(path);

            Assert.That(effective.Token, Is.EqualTo("from.file"));
            Assert.That(CredsFile.SqliteDbPath(effective, _dir), Is.EqualTo(Path.Combine(_dir, "data", "other.db")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DB_ENV, null);
        }
    }

    [Test]
    public void BootGuard_StopsAfterRepeatedFailedStartsUntilResetOrRetryTime()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var guard = new BootGuard(new HostPaths(_dir), time);

        for (var i = 0; i < BootGuard.MAX_ATTEMPTS; i++)
            Assert.That(guard.TryBeginAttempt(), Is.True);

        Assert.That(guard.TryBeginAttempt(), Is.False);
        Assert.That(guard.UntilRetry(), Is.EqualTo(BootGuard.RetryAfter));

        guard.Reset();
        Assert.That(guard.TryBeginAttempt(), Is.True);

        for (var i = 1; i < BootGuard.MAX_ATTEMPTS; i++)
            guard.TryBeginAttempt();
        Assert.That(guard.TryBeginAttempt(), Is.False);

        time.Advance(BootGuard.RetryAfter);
        Assert.That(guard.TryBeginAttempt(), Is.True);
        Assert.That(guard.TryBeginAttempt(), Is.True);
        Assert.That(guard.TryBeginAttempt(), Is.True);
        Assert.That(guard.TryBeginAttempt(), Is.False);

        // The clock moved back a day: the wait stays at most RetryAfter, and the next start may try.
        var earlier = new BootGuard(new HostPaths(_dir),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(time.GetUtcNow() - TimeSpan.FromDays(1)));
        Assert.That(earlier.UntilRetry(), Is.LessThanOrEqualTo(BootGuard.RetryAfter));
        Assert.That(earlier.TryBeginAttempt(), Is.True);
    }

    [Test]
    public async Task HostedCoordinator_RestartStopsTheHostAfterTheReplyDelay()
    {
        using var lifetime = new FakeLifetime();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var coord = new HostedCoordinator(lifetime, new DiscordSocketClient(), time);

        Assert.That(coord.RestartBot(), Is.True);

        // Whether or not the delay has started yet, less than the delay must not stop the host.
        time.Advance(HostedCoordinator.StopDelay - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        Assert.That(lifetime.Stopped, Is.False);

        // The delay starts on another thread, so advance until it has started and run out.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!lifetime.Stopped)
        {
            time.Advance(HostedCoordinator.StopDelay);
            await Task.Delay(10, cts.Token);
        }
    }

    private static bool IsInjected(Type t)
        => !t.IsPrimitive
           && t != typeof(string)
           && t != typeof(HttpContext)
           && t != typeof(CancellationToken)
           && t != typeof(GuildRef)
           && Nullable.GetUnderlyingType(t) is null
           && !t.Name.EndsWith("Request", StringComparison.Ordinal);

    private WebApplication Build(HostMode mode)
        => ApiHost.Build(
            new HostContext(mode, 0, 1),
            new HostPaths(Path.Combine(_dir, "data")),
            new ApiSettings { Port = 0 },
            new BotBridge(),
            new RestoreTarget(null));

    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Stopped
            => _stopped.Task.IsCompleted;

        public CancellationToken ApplicationStarted
            => CancellationToken.None;

        public CancellationToken ApplicationStopping
            => _stopping.Token;

        public CancellationToken ApplicationStopped
            => CancellationToken.None;

        public void StopApplication()
            => _stopped.TrySetResult();

        public void Dispose()
            => _stopping.Dispose();
    }
}
