using DryIoc;
using LinqToDB.Extensions;
using Microsoft.Extensions.DependencyInjection;
using NadekoBot.Modules.Music;
using NadekoBot.Modules.Music.Resolvers;
using NadekoBot.Modules.Music.Services;
using StackExchange.Redis;
using System.Net;
using System.Reflection;
using NadekoBot.Common.ModuleBehaviors;

namespace NadekoBot.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly string _responsesPath = "strings/responses";
    private static readonly string _commandsPath = "strings/commands";

    public static IContainer AddBotStringsServices(this IContainer svcs, BotCacheImplemenation botCache)
    {
        var curLoc = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var res = Path.Combine(curLoc, _responsesPath);
        var cmd = Path.Combine(curLoc, _commandsPath);

        svcs.AddSingleton<IStringsSource>(new LocalFileStringsSource(
            res,
            cmd
        ));
        
        if (botCache == BotCacheImplemenation.Memory)
        {
            svcs.AddSingleton<IBotStringsProvider, MemoryBotStringsProvider>();
        }
        else
        {
            svcs.AddSingleton<IBotStringsProvider, RedisBotStringsProvider>();
        }

        svcs.AddSingleton<IBotStrings, BotStrings>();

        return svcs;
    }

    public static IEnumerable<Type> GetConfigServiceTypes(Assembly a)
        => a.GetTypes().Where(static x => !x.IsAbstract && x.IsAssignableToGenericType(typeof(ConfigServiceBase<>)));

    public static IContainer AddConfigServices(this IContainer svcs, Assembly a)
    {
        foreach (var type in GetConfigServiceTypes(a))
        {
            svcs.RegisterMany([type],
                getServiceTypes: type => type.GetImplementedTypes(ReflectionTools.AsImplementedType.SourceType),
                getImplFactory: type => ReflectionFactory.Of(type, Reuse.Singleton));
        }

        return svcs;
    }


    public static IContainer AddMusic(this IContainer svcs)
    {
        svcs.RegisterMany<MusicService>(Reuse.Singleton);

        svcs.AddSingleton<ITrackResolveProvider, TrackResolveProvider>();
        svcs.AddSingleton<YtdlYoutubeResolver>();
        svcs.AddSingleton<InvidiousYoutubeResolver>();
        svcs.AddSingleton<ServiceYoutubeResolver>();
        svcs.AddSingleton<IYoutubeResolverFactory, YoutubeResolverFactory>();
        svcs.AddSingleton<ILocalTrackResolver, LocalTrackResolver>();
        svcs.AddSingleton<IRadioResolver, RadioResolver>();
        svcs.AddSingleton<ITrackCacher, TrackCacher>();

        return svcs;
    }

    public static IContainer AddCache(this IContainer cont, IBotCreds creds)
    {
        if (creds.BotCache == BotCacheImplemenation.Redis)
        {
            var conf = ConfigurationOptions.Parse(creds.RedisOptions);
            cont.AddSingleton<ConnectionMultiplexer>(ConnectionMultiplexer.Connect(conf));
            cont.AddSingleton<IBotCache, RedisBotCache>();
            cont.AddSingleton<IPubSub, RedisPubSub>();
        }
        else
        {
            cont.AddSingleton<IBotCache, MemoryBotCache>();
            cont.AddSingleton<IPubSub, EventPubSub>();
        }

        return cont
            .AddBotStringsServices(creds.BotCache);
    }

    public static IContainer AddHttpClients(this IContainer svcs)
    {
        IServiceCollection proxySvcs = new ServiceCollection();
        proxySvcs.AddHttpClient();
        proxySvcs.AddHttpClient(ExternalHttp.CLIENT_NAME, ExternalHttp.Configure)
            .ConfigurePrimaryHttpMessageHandler(ExternalHttp.CreateHandler);

        proxySvcs.AddHttpClient(ExternalHttp.OWNER_CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => svcs.Resolve<BotPolicy>().Managed
                ? ExternalHttp.CreateHandler()
                : new HttpClientHandler());

        // The owner sets the music service url, often a local address, so a self-hosted bot may reach the local network.
        // A managed bot reaches only the service which the manager set.
        proxySvcs.AddHttpClient(MusicServiceClient.HTTP_CLIENT_NAME, static c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => svcs.Resolve<BotPolicy>().Managed
                ? MusicServiceSetting.Default.CreateHandler()
                : new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });

        // A self-hosted bot keeps the system proxy, which some owners use for YouTube, behind the address check.
        proxySvcs.AddHttpClient(AudioFileCacheService.HTTP_CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => svcs.Resolve<BotPolicy>().Managed
                ? ExternalHttp.CreateHandler()
                : new SocketsHttpHandler
                {
                    Proxy = svcs.Resolve<PublicOnlyProxy>(),
                    UseProxy = true,
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = ExternalHttp.MAX_REDIRECTS,
                    ConnectTimeout = TimeSpan.FromSeconds(10)
                });

        // A managed bot with a YouTube proxy: YouTube traffic must leave through the IP which resolved the stream.
        proxySvcs.AddHttpClient(AudioFileCacheService.YOUTUBE_HTTP_CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                Proxy = svcs.Resolve<PublicOnlyProxy>(),
                UseProxy = true,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = ExternalHttp.MAX_REDIRECTS,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            });

        proxySvcs.AddHttpClient("memelist")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        proxySvcs.AddHttpClient("google:search")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

        proxySvcs.AddHttpClient("brave")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

        var prov = proxySvcs.BuildServiceProvider();

        svcs.RegisterDelegate<IHttpClientFactory>(_ => prov.GetRequiredService<IHttpClientFactory>());
        svcs.RegisterDelegate<HttpClient>(_ => prov.GetRequiredService<HttpClient>());

        return svcs;
    }

    public static IContainer AddLifetimeServices(this IContainer svcs, Assembly a)
    {
        Type[] types =
        [
            typeof(IExecOnMessage),
            typeof(IExecPreCommand),
            typeof(IExecPostCommand),
            typeof(IExecNoCommand),
            typeof(IInputTransformer),
            typeof(INService)
        ];

        foreach (var svc in a.GetTypes()
                     .Where(type => type.IsClass && types.Any(t => type.IsAssignableTo(t)) &&
                                    !type.HasAttribute<DIIgnoreAttribute>()
#if GLOBAL_NADEKO
                            && !type.HasAttribute<NoPublicBotAttribute>()
#endif
                     ))
        {
            svcs.RegisterMany([svc],
                getServiceTypes: type => type.GetImplementedTypes(ReflectionTools.AsImplementedType.SourceType),
                getImplFactory: type => ReflectionFactory.Of(type, Reuse.Singleton));
        }

        return svcs;
    }
}