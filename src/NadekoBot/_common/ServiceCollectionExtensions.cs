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
    public static IContainer AddBotStringsServices(this IContainer svcs, BotCacheImplemenation botCache)
    {
        if (botCache == BotCacheImplemenation.Memory)
        {
            svcs.AddSingleton<IStringsSource, LocalFileStringsSource>();
            svcs.AddSingleton<IBotStringsProvider, MemoryBotStringsProvider>();
        }
        else
        {
            svcs.AddSingleton<IStringsSource, LocalFileStringsSource>();
            svcs.AddSingleton<IBotStringsProvider, RedisBotStringsProvider>();
        }

        svcs.AddSingleton<IBotStrings, BotStrings>();

        return svcs;
    }

    public static IContainer AddConfigServices(this IContainer svcs, Assembly a)
    {
        
        foreach (var type in a.GetTypes()
                           .Where(x => !x.IsAbstract && x.IsAssignableToGenericType(typeof(ConfigServiceBase<>))))
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
                           .Where(type => type.IsClass && types.Any(t => type.IsAssignableTo(t)) && !type.HasAttribute<DIIgnoreAttribute>()
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