#nullable disable
using DryIoc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Common.Configs;
using NadekoBot.Common.ModuleBehaviors;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RunMode = Discord.Commands.RunMode;

namespace NadekoBot;

public sealed class Bot : IBot
{
    public DiscordSocketClient Client { get; }

    public IContainer Services { get; private set; }

    public int ShardId { get; }

    private readonly IBotCreds _creds;
    private readonly CommandService _commandService;
    private readonly DbService _db;

    private readonly IBotCredsProvider _credsProvider;

    private readonly Assembly[] _loadedAssemblies;
    private readonly Action<IContainer> _configureServices;
    // private readonly InteractionService _interactionService;

    public IntentState Intents { get; }

    private readonly bool _startKnewFlags;
    private int _refusedIntents;

    // A host which read the application's flags passes the privileged intents which Discord allows; null asks for all.
    public Bot(int shardId, int? totalShards, Action<IContainer> configureServices = null, IntentState intents = null)
        : this(shardId, CreateCredsProvider(shardId, totalShards), configureServices, intents, exitsOnRefusedIntents: false)
    {
    }

    // The self-hosted program: asks only for the intents which Discord allows, and exits when Discord refuses one.
    public static async Task<Bot> CreateAsync(int shardId, int? totalShards)
    {
        var credsProvider = CreateCredsProvider(shardId, totalShards);
        var creds = credsProvider.GetCreds();

        // Coordinated shards ask for all intents, so one bad answer from Discord cannot turn off features on a shard.
        var coordinated = IsCoordinated;
        var intents = creds.UsePrivilegedIntents && !coordinated
            ? await SelfHostIntents.ReadAsync(creds.Token)
            : null;

        return new Bot(shardId, credsProvider, null, intents, exitsOnRefusedIntents: !coordinated);
    }

    private static BotCredsProvider CreateCredsProvider(int shardId, int? totalShards)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shardId, 0);
        LogSetup.SetupLogger(shardId, null);
        return new BotCredsProvider(totalShards);
    }

    private static bool IsCoordinated
        => Environment.GetEnvironmentVariable("NADEKOBOT_IS_COORDINATED") == "1";

    private Bot(
        int shardId,
        BotCredsProvider credsProvider,
        Action<IContainer> configureServices,
        IntentState intents,
        bool exitsOnRefusedIntents)
    {
        _configureServices = configureServices;
        ShardId = shardId;
        _credsProvider = credsProvider;
        _creds = _credsProvider.GetCreds();
        _startKnewFlags = intents is not null;

        LogSetup.SetupLogger(shardId, _creds);
        Log.Information("Pid: {ProcessId}", Environment.ProcessId);

        _db = new NadekoDbService(_credsProvider);

        var messageCacheSize =
#if GLOBAL_NADEKO
            0;
#else
            50;
#endif

        Intents = _creds.UsePrivilegedIntents ? intents ?? IntentState.All : IntentState.None;
        if (!_creds.UsePrivilegedIntents)
            Log.Warning("You are not using privileged intents. Some features will not work properly");
        else if (!Intents.IsAll)
            Log.Warning("Discord does not allow these privileged intents, so the bot connects without them: {Intents}",
                Intents.MissingNames());

        if (!Intents.MessageContent)
            Log.Information("Without Message Content, users run commands with a mention, for example @Bot help");

        Client = new(new()
        {
            MessageCacheSize = messageCacheSize,
            LogLevel = LogSeverity.Warning,
            ConnectionTimeout = int.MaxValue,
            TotalShards = _creds.TotalShards,
            ShardId = shardId,
            AlwaysDownloadUsers = false,
            AlwaysResolveStickers = false,
            AlwaysDownloadDefaultStickers = false,
            GatewayIntents = Intents.GatewayIntents,
            LogGatewayIntentWarnings = false,
            FormatUsersInBidirectionalUnicode = false,
            DefaultRetryMode = RetryMode.Retry502,
#if GLOBAL_NADEKO
            IncludeRawPayloadOnGatewayErrors = true,
#endif
        });

        _commandService = new(new()
        {
            CaseSensitiveCommands = false,
            DefaultRunMode = RunMode.Sync,
        });

        // _interactionService = new(Client.Rest);

        Client.Log += Client_Log;
        if (exitsOnRefusedIntents)
            Client.Disconnected += OnDisconnected;

        _loadedAssemblies =
        [
            typeof(Bot).Assembly // bot
        ];
    }


    public IReadOnlyList<ulong> GetCurrentGuildIds()
        => Client.Guilds.Select(x => x.Id).ToList().AsReadOnly();

    private Task AddServices()
    {
        var startTime = Stopwatch.GetTimestamp();

        var svcs = new Container();

        svcs.AddSingleton<IBotCreds>(_ => _credsProvider.GetCreds());
        svcs.AddSingleton<DbService, DbService>(_db);
        svcs.AddSingleton<IBotCredsProvider>(_credsProvider);
        svcs.AddSingleton<DiscordSocketClient>(Client);
        svcs.AddSingleton<CommandService>(_commandService);
        svcs.AddSingleton<Bot>(this);
        svcs.AddSingleton<IBot>(this);
        svcs.AddSingleton(new BotPolicy(false));
        svcs.AddSingleton(Intents);

        svcs.AddSingleton<ISeria, JsonSeria>();
        svcs.AddSingleton<IConfigSeria, YamlSeria>();
        svcs.AddSingleton<IMemoryCache, MemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        svcs.AddSingleton<IBehaviorHandler, BehaviorHandler>();


        foreach (var a in _loadedAssemblies)
        {
            svcs.AddConfigServices(a)
                .AddLifetimeServices(a);
        }

        svcs.AddMusic()
            .AddCache(_creds)
            .AddHttpClients();

        if (!IsCoordinated)
        {
            svcs.AddSingleton<ICoordinator, SingleProcessCoordinator>();
        }
        else
        {
            svcs.AddSingleton<RemoteGrpcCoordinator>();
            svcs.AddSingleton<ICoordinator>(_ => svcs.GetRequiredService<RemoteGrpcCoordinator>());
            svcs.AddSingleton<IReadyExecutor>(_ => svcs.GetRequiredService<RemoteGrpcCoordinator>());
        }

        _configureServices?.Invoke(svcs);

        svcs.AddSingleton<IServiceProvider>(svcs);

        //initialize Services
        Services = svcs;
        Services.GetRequiredService<IBehaviorHandler>().Initialize();

        foreach (var a in _loadedAssemblies)
        {
            LoadTypeReaders(a);
        }

        Log.Information("All services loaded in {ServiceLoadTime:F2}s",
            Stopwatch.GetElapsedTime(startTime).TotalSeconds);

        return Task.CompletedTask;
    }

    private void LoadTypeReaders(Assembly assembly)
    {
        var filteredTypes = assembly.GetExportedTypes()
            .Where(x => x.IsSubclassOf(typeof(TypeReader))
                        && x.BaseType?.GetGenericArguments().Length > 0
                        && !x.IsAbstract);

        foreach (var ft in filteredTypes)
        {
            var baseType = ft.BaseType;
            if (baseType is null)
                continue;

            var typeReader = (TypeReader)ActivatorUtilities.CreateInstance(Services, ft);
            var typeArgs = baseType.GetGenericArguments();
            _commandService.AddTypeReader(typeArgs[0], typeReader);
        }
    }

    private async Task LoginAsync(string token)
    {
        var clientReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task SetClientReady()
        {
            clientReady.TrySetResult(true);
            try
            {
                foreach (var chan in await Client.GetDMChannelsAsync())
                    await chan.CloseAsync();
            }
            catch
            {
                // ignored
            }
        }

        //connect
        Log.Information("Shard {ShardId} logging in ...", Client.ShardId);
        try
        {
            Client.Ready += SetClientReady;

            await Client.LoginAsync(TokenType.Bot, token);
            await Client.StartAsync();
        }
        catch (HttpException ex)
        {
            LoginErrorHandler.Handle(ex);
            Helpers.ReadErrorAndExit(101);
        }
        catch (Exception ex)
        {
            LoginErrorHandler.Handle(ex);
            Helpers.ReadErrorAndExit(5);
        }

        await clientReady.Task.ConfigureAwait(false);
        Client.Ready -= SetClientReady;

        Client.JoinedGuild += Client_JoinedGuild;
        Client.LeftGuild += Client_LeftGuild;

        // _ = Client.SetStatusAsync(UserStatus.Online);
        Log.Information("Shard {ShardId} logged in", Client.ShardId);
    }

    private Task Client_LeftGuild(SocketGuild arg)
    {
        Log.Information("Left server: {GuildName} [{GuildId}] [available: {GuildAvailable}]", arg?.Name, arg?.Id, (arg as IGuild)?.Available);
        return Task.CompletedTask;
    }

    private Task Client_JoinedGuild(SocketGuild arg)
    {
        Log.Information("Joined server: {GuildName} [{GuildId}]", arg.Name, arg.Id);
        return Task.CompletedTask;
    }

    public async Task RunAsync()
    {
        if (ShardId == 0)
            await _db.SetupAsync();

        var startTime = Stopwatch.GetTimestamp();

        var loginTask = LoginAsync(_creds.Token);
        var servicesTask = Task.Run(async () =>
        {
            Log.Information("Shard {ShardId} loading services...", ShardId);
            await AddServices();

            foreach (var a in _loadedAssemblies)
            {
                await _commandService.AddModulesAsync(a, Services);
            }
        });

        try
        {
            await Task.WhenAll(loginTask, servicesTask);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup");
            Helpers.ReadErrorAndExit(103);
        }

        await using (var uow = _db.GetDbContext())
        {
            var bot = Client.CurrentUser;
            uow.EnsureUserCreated(bot.Id, bot.Username, bot.AvatarId);
        }

        Log.Information("Shard {ShardId} connected in {Elapsed:F2}s",
            Client.ShardId,
            Stopwatch.GetElapsedTime(startTime).TotalSeconds);

        var commandHandler = Services.GetRequiredService<CommandHandler>();

        await EnsureBotOwnershipAsync();

        await commandHandler.InitializeAsync();

        _ = Task.Run(ExecuteReadySubscriptions);

        await commandHandler.StartHandling();

        Log.Information("Shard {ShardId} ready", Client.ShardId);
    }

    private async ValueTask EnsureBotOwnershipAsync()
    {
        try
        {
            if (_creds.OwnerIds.Count != 0)
                return;

            Log.Information("Initializing Owner Id...");
            var info = await Client.GetApplicationInfoAsync();
            _credsProvider.ModifyCredsFile(x => x.OwnerIds = [info.Owner.Id]);
        }
        catch (Exception ex)
        {
            Log.Warning("Getting application info failed: {ErrorMessage}", ex.Message);
        }
    }

    private Task ExecuteReadySubscriptions()
    {
        var readyExecutors = Services.GetServices<IReadyExecutor>();
        var tasks = readyExecutors.Select(async toExec =>
        {
            try
            {
                await toExec.OnReadyAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Failed running OnReadyAsync method on {Type} type: {Message}",
                    toExec.GetType().Name,
                    ex.Message);
            }
        });

        return tasks.WhenAll();
    }

    private Task Client_Log(LogMessage arg)
    {
        if (arg.Message?.Contains("unknown dispatch", StringComparison.InvariantCultureIgnoreCase) ?? false)
            return Task.CompletedTask;

        if (arg.Exception is { InnerException: WebSocketClosedException { CloseCode: SelfHostIntents.DISALLOWED_INTENTS_CLOSE_CODE } })
        {
            Log.Error("""
                      Discord refused a privileged intent which the bot asked for (gateway close 4014).
                      To use all features, turn on the privileged intents:
                      1. Open https://discord.com/developers/applications/ and select your application.
                      2. Click Bot and turn on Presence Intent, Server Members Intent and Message Content Intent.
                      3. Save the changes and restart the bot.
                      A bot in 100 or more servers must apply to Discord for each intent.
                      """);
            return Task.CompletedTask;
        }

        if (arg.Exception is not null)
        {
            Log.Warning(arg.Exception, "{ErrorSource} | {ErrorMessage}", arg.Source, arg.Message);
#if GLOBAL_NADEKO
            if (arg.Exception.Data is { } data)
            {
                Log.Warning("Full error: {FullError}",
                    JsonSerializer.Serialize(arg.Exception.Data,
                        new JsonSerializerOptions()
                        {
                            WriteIndented = true
                        }));
            }
#endif
        }
        else
        {
            Log.Warning("{ErrorSource} | {ErrorMessage}", arg.Source, arg.Message);
        }

        return Task.CompletedTask;
    }

    // Discord refuses the same intents on each reconnect, and each try uses one of the bot's daily logins.
    private Task OnDisconnected(Exception ex)
    {
        if ((ex as WebSocketClosedException)?.CloseCode != SelfHostIntents.DISALLOWED_INTENTS_CLOSE_CODE
            || Interlocked.Exchange(ref _refusedIntents, 1) == 1)
            return Task.CompletedTask;

        _ = Task.Run(ExitForRefusedIntentsInternalAsync);
        return Task.CompletedTask;
    }

    private async Task ExitForRefusedIntentsInternalAsync()
    {
        try
        {
            await Client.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The gateway did not close cleanly");
        }

        var flags = await SelfHostIntents.ReadAsync(_creds.Token);
        if (flags is not null && flags.LacksAnyOf(Intents))
        {
            Log.Error("Discord does not allow these privileged intents now: {Intents}. "
                      + "Turn them on in the Developer Portal, or start the bot again to run without them",
                flags.MissingNames());
        }

        if (!SelfHostIntents.ExitsAtOnce(Intents, _startKnewFlags, flags))
        {
            Log.Error("The bot exits in {Delay}. Then start it again", SelfHostIntents.RetryDelay);
            await Task.Delay(SelfHostIntents.RetryDelay);
        }

        Helpers.ReadErrorAndExit(SelfHostIntents.EXIT_DISALLOWED_INTENTS);
    }

    public async Task RunAndBlockAsync()
    {
        await RunAsync();
        await Task.Delay(-1);
    }
}