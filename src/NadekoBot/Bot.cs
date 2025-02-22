#nullable disable
using DryIoc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Common.Configs;
using NadekoBot.Common.ModuleBehaviors;
using System.Diagnostics;
using System.Reflection;
using RunMode = Discord.Commands.RunMode;

namespace NadekoBot;

public sealed class Bot : IBot
{
    public DiscordSocketClient Client { get; }

    private IContainer Services { get; set; }

    public bool IsReady { get; private set; }
    public int ShardId { get; }

    private readonly IBotCreds _creds;
    private readonly CommandService _commandService;
    private readonly DbService _db;

    private readonly IBotCredsProvider _credsProvider;

    private readonly Assembly[] _loadedAssemblies;
    // private readonly InteractionService _interactionService;

    public Bot(int shardId, int? totalShards)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shardId, 0);

        LogSetup.SetupLogger(shardId, null);
        ShardId = shardId;
        _credsProvider = new BotCredsProvider(totalShards);
        _creds = _credsProvider.GetCreds();

        LogSetup.SetupLogger(shardId, _creds);
        Log.Information("Pid: {ProcessId}", Environment.ProcessId);

        _db = new NadekoDbService(_credsProvider);

        var messageCacheSize =
#if GLOBAL_NADEKO
            0;
#else
            50;
#endif

        if (!_creds.UsePrivilegedIntents)
            Log.Warning("You are not using privileged intents. Some features will not work properly");

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
            GatewayIntents = _creds.UsePrivilegedIntents
                ? GatewayIntents.All
                : GatewayIntents.AllUnprivileged,
            LogGatewayIntentWarnings = false,
            FormatUsersInBidirectionalUnicode = false,
            DefaultRetryMode = RetryMode.Retry502
        });

        _commandService = new(new()
        {
            CaseSensitiveCommands = false,
            DefaultRunMode = RunMode.Sync,
        });

        // _interactionService = new(Client.Rest);

        Client.Log += Client_Log;
        _loadedAssemblies =
        [
            typeof(Bot).Assembly // bot
        ];
    }


    public IReadOnlyList<ulong> GetCurrentGuildIds()
        => Client.Guilds.Select(x => x.Id).ToList().AsReadOnly();

    private async Task AddServices()
    {
        var startingGuildIdList = GetCurrentGuildIds().ToList();
        var startTime = Stopwatch.GetTimestamp();
        var bot = Client.CurrentUser;

        await using (var uow = _db.GetDbContext())
        {
            uow.EnsureUserCreated(bot.Id, bot.Username, bot.Discriminator, bot.AvatarId);
        }

        // var svcs = new StandardKernel(new NinjectSettings()
        // {
        //     // ThrowOnGetServiceNotFound = true,
        //     ActivationCacheDisabled = true,
        // });

        var svcs = new Container();

        svcs.AddSingleton<IBotCreds>(_ => _credsProvider.GetCreds());
        svcs.AddSingleton<DbService, DbService>(_db);
        svcs.AddSingleton<IBotCredsProvider>(_credsProvider);
        svcs.AddSingleton<DiscordSocketClient>(Client);
        svcs.AddSingleton<CommandService>(_commandService);
        svcs.AddSingleton<Bot>(this);
        svcs.AddSingleton<IBot>(this);

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

        if (Environment.GetEnvironmentVariable("NADEKOBOT_IS_COORDINATED") != "1")
        {
            svcs.AddSingleton<ICoordinator, SingleProcessCoordinator>();
        }
        else
        {
            svcs.AddSingleton<RemoteGrpcCoordinator>();
            svcs.AddSingleton<ICoordinator>(_ => svcs.GetRequiredService<RemoteGrpcCoordinator>());
            svcs.AddSingleton<IReadyExecutor>(_ => svcs.GetRequiredService<RemoteGrpcCoordinator>());
        }

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
        Log.Information("Left server: {GuildName} [{GuildId}]", arg?.Name, arg?.Id);
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

        await LoginAsync(_creds.Token);

        Log.Information("Shard {ShardId} loading services...", Client.ShardId);
        try
        {
            await AddServices();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding services");
            Helpers.ReadErrorAndExit(103);
        }

        Log.Information("Shard {ShardId} connected in {Elapsed:F2}s",
            Client.ShardId,
            Stopwatch.GetElapsedTime(startTime).TotalSeconds);
        var commandHandler = Services.GetRequiredService<CommandHandler>();

        // start handling messages received in commandhandler
        await commandHandler.StartHandling();

        foreach (var a in _loadedAssemblies)
        {
            await _commandService.AddModulesAsync(a, Services);
        }

        // await _interactionService.AddModulesAsync(typeof(Bot).Assembly, Services);
        IsReady = true;

        await EnsureBotOwnershipAsync();
        _ = Task.Run(ExecuteReadySubscriptions);
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
                
                Environment.Exit(9);
            }
        });

        return tasks.WhenAll();
    }

    private Task Client_Log(LogMessage arg)
    {
        if (arg.Message?.Contains("unknown dispatch", StringComparison.InvariantCultureIgnoreCase) ?? false)
            return Task.CompletedTask;

        if (arg.Exception is { InnerException: WebSocketClosedException { CloseCode: 4014 } })
        {
            Log.Error("""
                      Login failed.

                      *** Please enable privileged intents ***

                      Certain Nadeko features require Discord's privileged gateway intents.
                      These include greeting and goodbye messages, as well as creating the Owner message channels for DM forwarding.

                      How to enable privileged intents:
                      1. Head over to the Discord Developer Portal https://discord.com/developers/applications/
                      2. Select your Application.
                      3. Click on `Bot` in the left side navigation panel, and scroll down to the intents section.
                      4. Enable all intents.
                      5. Restart your bot.

                      Read this only if your bot is in 100 or more servers:

                      You'll need to apply to use the intents with Discord, but for small selfhosts, all that is required is enabling the intents in the developer portal.
                      Yes, this is a new thing from Discord, as of October 2020. No, there's nothing we can do about it. Yes, we're aware it worked before.
                      While waiting for your bot to be accepted, you can change the 'usePrivilegedIntents' inside your creds.yml to 'false', although this will break many of the nadeko's features
                      """);
            return Task.CompletedTask;
        }

        if (arg.Exception is not null)
            Log.Warning(arg.Exception, "{ErrorSource} | {ErrorMessage}", arg.Source, arg.Message);
        else
            Log.Warning("{ErrorSource} | {ErrorMessage}", arg.Source, arg.Message);
        return Task.CompletedTask;
    }

    public async Task RunAndBlockAsync()
    {
        await RunAsync();
        await Task.Delay(-1);
    }
}