using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NadekoBot.Hosted.Endpoints;
using NadekoBot.Modules.Administration.Services;
using NadekoBot.Modules.NadekoExpressions;
using NadekoBot.Modules.Utility;
using NadekoBot.Modules.Xp;
using NadekoBot.Modules.Xp.Services;

namespace NadekoBot.Hosted;

public static class ApiHost
{
    public const string DISCORD_CALL_POLICY = "discord-call";

    private const ApiAccess GUILD_ACCESS = ApiAccess.Key | ApiAccess.Bot | ApiAccess.Guild;
    private const ApiAccess OWNER_ACCESS = ApiAccess.Key | ApiAccess.Bot;

    private static readonly string _botVersion = StatsService.BotVersion;

    public static WebApplication Build(
        HostContext host,
        HostPaths paths,
        ApiSettings settings,
        BotBridge bridge,
        RestoreTarget restore,
        LogStreamService? logs = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(IPAddress.Parse(settings.Address), host.ApiPort(settings.Port));
            o.Limits.MaxRequestBodySize = ApiLimits.MAX_REQUEST_BODY_BYTES;
        });

        var services = builder.Services;
        services.ConfigureHttpJsonOptions(static o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
            o.SerializerOptions.RespectNullableAnnotations = true;
            o.SerializerOptions.RespectRequiredConstructorParameters = true;
        });

        services.AddRateLimiter(static o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(DISCORD_CALL_POLICY, static http => RateLimitPartition.GetFixedWindowLimiter(
                GuildRef.TryGet(http, out var guild) ? guild.Id : 0,
                static _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ApiLimits.DISCORD_CALLS_PER_GUILD,
                    Window = ApiLimits.DiscordCallWindow,
                    QueueLimit = 0
                }));
        });

        services.AddHttpClient();
        services.AddSingleton(host);
        services.AddSingleton(paths);
        // The plaintext key lives only in ApiKey (as a hash); a rotation would leave a stale copy here.
        services.AddSingleton(settings.WithKey(string.Empty));
        services.AddSingleton(bridge);
        services.AddSingleton(restore);
        services.AddSingleton(bridge.Time);
        services.AddSingleton(new ApiKey(settings.ApiKey));
        services.AddSingleton(new LastUpdate(UpdateResultFile.Read(paths.UpdateResult)));
        services.AddSingleton<BootGuard>();
        services.AddSingleton<CredsStore>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<ServerImportService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<IProcessRestarter, ProcessRestarter>();
        // The program passes the instance which the logger already writes to.
        if (logs is not null)
            services.AddSingleton(logs);
        else
            services.AddSingleton(static sp => new LogStreamService(sp.GetRequiredService<TimeProvider>()));

        AddBotService<ShardData>(services);
        AddBotService<ICoordinator>(services);
        AddBotService<IStatsService>(services);
        AddBotService<DiscordSocketClient>(services);
        AddBotService<GreetService>(services);
        AddBotService<NadekoExpressionsService>(services);
        AddBotService<IQuoteService>(services);
        AddBotService<UserPunishService>(services);
        AddBotService<XpService>(services);
        AddBotService<XpFormulaService>(services);
        AddBotService<DbService>(services);
        AddBotService<IBotCredsProvider>(services);
        services.AddSingleton(static sp => sp.GetRequiredService<BotBridge>().Feed);
        services.AddSingleton(static sp => sp.GetRequiredService<BotBridge>().Activity);
        services.AddSingleton(static sp =>
        {
            var bridge = sp.GetRequiredService<BotBridge>();
            return new ConfigRegistry(bridge.GetAll<IConfigService>(), bridge.Get<BotPolicy>());
        });

        var app = builder.Build();
        app.UseMiddleware<ApiErrorMiddleware>();
        app.UseMiddleware<ApiGateMiddleware>();
        app.UseRateLimiter();
        app.MapRoutes(host.Mode);
        return app;
    }

    public static void MapRoutes(this IEndpointRouteBuilder app, HostMode mode)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", static (BotBridge bridge, HostContext host, ApiKey key, LastUpdate update) =>
        {
            var health = bridge.Health(key.IsSet);
            return Results.Json(
                new HealthDto(health.Status, host.ShardId, host.TotalShards, _botVersion, health.Reason, update.Result),
                ApiJsonContext.Default.HealthDto);
        }).WithMetadata(new ApiAccessMetadata(ApiAccess.Open));

        api.MapGroup("/guilds/{" + ApiGateMiddleware.GUILD_ID_ROUTE_VALUE + "}")
            .WithMetadata(new ApiAccessMetadata(GUILD_ACCESS))
            .MapGuildEndpoints();

        if (mode == HostMode.Coordinated)
            return;

        api.MapGroup(string.Empty)
            .WithMetadata(new ApiAccessMetadata(ApiAccess.KeyOnceSet))
            .MapCredsEndpoint();

        api.MapGroup(string.Empty)
            .WithMetadata(new ApiAccessMetadata(ApiAccess.Key))
            .MapSystemEndpoints()
            .MapLogEndpoints()
            .MapUpdateEndpoint()
            .MapBackupFileEndpoints()
            .MapBackupWriteEndpoints();

        api.MapGroup(string.Empty)
            .WithMetadata(new ApiAccessMetadata(OWNER_ACCESS))
            .MapOwnerEndpoints();

        api.MapGroup("/guilds/{" + ApiGateMiddleware.GUILD_ID_ROUTE_VALUE + "}")
            .WithMetadata(new ApiAccessMetadata(GUILD_ACCESS))
            .MapOwnerGuildEndpoints();
    }

    // The bot's container exists only after Bot.RunAsync, so the web app resolves bot services through the bridge.
    private static void AddBotService<T>(IServiceCollection services) where T : class
        => services.AddSingleton(static sp => sp.GetRequiredService<BotBridge>().Get<T>());
}
