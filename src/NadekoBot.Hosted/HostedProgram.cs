using Discord.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NadekoBot.Hosted;

public sealed record HostArgs(int ShardId, int? TotalShards)
{
    public static bool TryParse(string[] args, out HostArgs parsed, out string? error)
    {
        parsed = new(0, null);
        error = null;

        if (args.Length == 0)
            return true;

        if (args.Length > (args[0] == "run" ? 1 : 2))
        {
            error = $"Too many arguments: {string.Join(' ', args)}";
            return false;
        }

        if (args[0] == "run")
            return true;

        if (!int.TryParse(args[0], out var shardId) || shardId < 0)
        {
            error = $"Invalid first argument (shard id): {args[0]}";
            return false;
        }

        int? totalShards = null;
        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out var total) || total < 1 || shardId >= total)
            {
                error = $"Invalid second argument (total shards): {args[1]}";
                return false;
            }

            totalShards = total;
        }

        parsed = new(shardId, totalShards);
        return true;
    }
}

public static class HostedProgram
{
    public const int EXIT_CONFIG_ERROR = 2;

    private const string DATA_DIR = "data";
    private const int AUTHENTICATION_FAILED_CLOSE_CODE = 4004;
    private const int DISALLOWED_INTENTS_CLOSE_CODE = 4014;

    private static readonly TimeSpan _tokenCheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _gatewayStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromMinutes(15);

    public static HostMode ModeFromEnvironment()
        => Environment.GetEnvironmentVariable(HostContext.COORDINATED_ENV) == "1"
            ? HostMode.Coordinated
            : HostMode.Standalone;

    public static async Task<int> RunAsync(HostArgs args)
    {
        var mode = ModeFromEnvironment();
        // Coordinated shards do not serve /api/logs, so they keep no lines.
        var logs = mode == HostMode.Standalone ? new LogStreamService(TimeProvider.System) : null;
        LogSetup.ExtraSink = logs?.Sink;
        LogSetup.SetupLogger(args.ShardId, null);

        var cwd = Directory.GetCurrentDirectory();
        var paths = new HostPaths(Path.Combine(cwd, DATA_DIR));
        var creds = CredsFile.LoadEffective(paths.Creds);
        var host = new HostContext(mode, args.ShardId, args.TotalShards ?? Math.Max(creds.TotalShards, 1));
        if (host.ShardId >= host.TotalShards)
        {
            Log.Fatal("Shard id {ShardId} is not below the total shard count {TotalShards}", host.ShardId, host.TotalShards);
            return EXIT_CONFIG_ERROR;
        }

        ApiSettings settings;
        try
        {
            settings = ApiSettingsFile.LoadOrCreate(paths.ApiSettings);
        }
        catch (InvalidDataException ex)
        {
            Log.Fatal("{Error}. Fix or delete the file", ex.Message);
            return EXIT_CONFIG_ERROR;
        }

        logs?.AddSecrets(creds.Token, settings.ApiKey);

        if (mode == HostMode.Coordinated && !settings.HasKey)
        {
            Log.Warning("No apiKey in {Path}. Shard {ShardId} runs without the REST API",
                paths.ApiSettings,
                args.ShardId);
            await new Bot(args.ShardId, args.TotalShards, ManagedPolicy.Register).RunAndBlockAsync();
            return 0;
        }

        var lastShardId = mode == HostMode.Coordinated ? host.TotalShards - 1 : 0;
        if (settings.Validate(lastShardId) is { } invalid)
        {
            Log.Fatal("Invalid {Path}: {Error}", paths.ApiSettings, invalid);
            return EXIT_CONFIG_ERROR;
        }

        var dbPath = CredsFile.SqliteDbPath(creds, cwd);
        if (mode == HostMode.Standalone)
        {
            if (!PendingRestore.Apply(paths, dbPath))
                return EXIT_CONFIG_ERROR;

            UpdateService.DiscardLeftovers(paths);
        }

        var token = creds.Token ?? string.Empty;
        var bridge = new BotBridge();
        bridge.SetPhase(string.IsNullOrWhiteSpace(token) ? BotPhase.NoToken : BotPhase.Starting);

        var app = ApiHost.Build(host, paths, settings, bridge, new RestoreTarget(dbPath), logs);
        await using var appScope = new BoundedDisposal(app, _gatewayStopTimeout);
        await app.StartAsync();

        Log.Information("REST API listening on http://{Address}:{Port}",
            settings.Address,
            host.ApiPort(settings.Port));

        var lifetime = app.Lifetime;
        var boot = mode == HostMode.Standalone ? app.Services.GetRequiredService<BootGuard>() : null;

        var check = mode == HostMode.Standalone && !string.IsNullOrWhiteSpace(token)
            ? await CheckTokenInternalAsync(app.Services.GetRequiredService<IHttpClientFactory>(),
                token,
                lifetime.ApplicationStopping)
            : TokenCheck.Unknown;

        // Creds or a restart can stop the host during the token check; then nothing may start or count an attempt.
        if (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            Log.Information("The API is stopping. The bot does not start");
        }
        else if (string.IsNullOrWhiteSpace(token))
        {
            if (mode == HostMode.Standalone)
            {
                boot?.KeepUpdate();
                Log.Information("No token yet. Serving the API until one is set with PUT /api/creds");
            }
            else
                Log.Error("No token in {Path}. Shard {ShardId} serves only the API", paths.Creds, args.ShardId);
        }
        else if (check == TokenCheck.Invalid)
        {
            bridge.SetPhase(BotPhase.InvalidToken);
            boot?.KeepUpdate();
            Log.Error("Discord rejected the token. Serving the API until a new one is set with PUT /api/creds");
        }
        else if (boot is not null && !boot.TryBeginAttempt())
        {
            bridge.SetFailed(FailureReason.STARTUP_FAILED);
            var retryIn = boot.UntilRetry();
            Log.Error("The bot failed to start {Attempts} times in a row. Serving the API and trying again in {RetryIn}",
                BootGuard.MAX_ATTEMPTS,
                retryIn);
            _ = RetryLaterInternalAsync(retryIn, bridge.Time, lifetime);
        }
        else
        {
            _ = Task.Run(() => StartBotInternalAsync(args, mode, paths, bridge, boot, lifetime));
            _ = ExitIfNotReadyInternalAsync(bridge, lifetime);
        }

        await app.WaitForShutdownAsync();
        await StopGatewayInternalAsync(bridge);
        return 0;
    }

    // Stopping hands the retry to the supervisor, which starts a clean process.
    private static async Task RetryLaterInternalAsync(TimeSpan delay, TimeProvider time, IHostApplicationLifetime lifetime)
    {
        try
        {
            await Task.Delay(delay, time, lifetime.ApplicationStopping);
            ProcessRestarter.StopForSupervisor(lifetime);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // A login hung by an outage would stay "starting" forever; the supervisor or coordinator starts a new process.
    private static async Task ExitIfNotReadyInternalAsync(BotBridge bridge, IHostApplicationLifetime lifetime)
    {
        try
        {
            await Task.Delay(_startupTimeout, bridge.Time, lifetime.ApplicationStopping);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (bridge.Phase != BotPhase.Starting)
            return;

        Log.Fatal("The bot was not ready after {Timeout}", _startupTimeout);
        FlushAndExit(1);
    }

    private static async Task<TokenCheck> CheckTokenInternalAsync(IHttpClientFactory factory, string token, CancellationToken ct)
    {
        var http = factory.CreateClient();
        http.Timeout = _tokenCheckTimeout;
        try
        {
            return await new DiscordTokenChecker(http).CheckAsync(token, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return TokenCheck.Unknown;
        }
    }

    private static async Task StartBotInternalAsync(
        HostArgs args,
        HostMode mode,
        HostPaths paths,
        BotBridge bridge,
        BootGuard? boot,
        IHostApplicationLifetime lifetime)
    {
        try
        {
            // The bot's own restart starts the NadekoBot launcher, which this package does not ship.
            var bot = mode == HostMode.Standalone
                ? new Bot(args.ShardId,
                    args.TotalShards,
                    svcs =>
                    {
                        ManagedPolicy.Register(svcs);
                        HostedCoordinator.Register(svcs, lifetime, bridge.Time);
                    })
                : new Bot(args.ShardId, args.TotalShards, ManagedPolicy.Register);

            var client = bot.Client;
            bridge.AttachClient(client);
            client.Disconnected += ex => OnDisconnectedInternal(ex, client, bridge, boot);
            client.Connected += () =>
            {
                bridge.MarkConnected();
                return Task.CompletedTask;
            };

            await bot.RunAsync();

            if (client.ConnectionState != ConnectionState.Connected)
                bridge.MarkDisconnected();

            // Coordinated shards do not serve /api/feed or /activity, so they do no feed or activity work.
            CommandFeedService? feed = null;
            GuildActivityService? activity = null;
            if (mode == HostMode.Standalone)
            {
                var commandHandler = bot.Services.GetRequiredService<ICommandHandler>();
                feed = new CommandFeedService(commandHandler, bridge.Time);
                activity = new GuildActivityService(paths.Activity, bridge.Time);
                activity.Subscribe(commandHandler, client);
            }

            if (bridge.SetReady(bot.Services, feed, activity))
                boot?.MarkReady();
        }
        catch (Exception ex)
        {
            if (bridge.Phase is BotPhase.Failed or BotPhase.InvalidToken)
                return;

            // Half-started bot services keep running in this process, so only a new process starts clean.
            Log.Fatal(ex, "The bot failed to start");
            FlushAndExit(1);
        }
    }

    // The bot's logger can send to Seq in batches, so the line which explains the exit must be flushed first.
    public static void FlushAndExit(int code)
    {
        Log.CloseAndFlush();
        Environment.Exit(code);
    }

    // Discord closes the gateway with these codes on every reconnect, so the client stops instead of retrying.
    private static Task OnDisconnectedInternal(
        Exception ex,
        DiscordSocketClient client,
        BotBridge bridge,
        BootGuard? boot)
    {
        bridge.MarkDisconnected();

        switch ((ex as WebSocketClosedException)?.CloseCode)
        {
            case DISALLOWED_INTENTS_CLOSE_CODE:
                Log.Error("Discord refused the privileged intents. Enable them in the developer portal and restart");
                bridge.SetFailed(FailureReason.DISALLOWED_INTENTS);
                break;
            case AUTHENTICATION_FAILED_CLOSE_CODE:
                Log.Error("Discord rejected the token. Set a new one with PUT /api/creds");
                bridge.SetPhase(BotPhase.InvalidToken);
                break;
            default:
                return Task.CompletedTask;
        }

        // Gateway handlers do no I/O; the reset deletes files. Discord accepted the build, so an update on trial is kept.
        _ = Task.Run(async () =>
        {
            boot?.Reset();
            boot?.KeepUpdate();
            await client.StopAsync();
        });
        return Task.CompletedTask;
    }

    private static async Task StopGatewayInternalAsync(BotBridge bridge)
    {
        bridge.FeedOrNull?.Dispose();
        if (bridge.ActivityOrNull is { } activity)
        {
            try
            {
                await activity.StopAsync().WaitAsync(_gatewayStopTimeout);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "The server activity was not written before the exit");
            }
        }

        if (bridge.Client is not { } client)
            return;

        try
        {
            await client.StopAsync().WaitAsync(_gatewayStopTimeout);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The gateway did not close cleanly");
        }
    }
}

// The web container also disposes the bot services it hands out; a hung Discord client must not block the exit.
public readonly struct BoundedDisposal(IAsyncDisposable target, TimeSpan timeout) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            await target.DisposeAsync().AsTask().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Log.Warning("Disposing the host took longer than {Timeout}. Exiting anyway", timeout);
        }
    }
}
