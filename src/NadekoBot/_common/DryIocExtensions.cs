using DryIoc;

namespace NadekoBot.Extensions;

public static class DryIocExtensions
{
    public static IContainer AddSingleton<TSvc, TImpl>(this IContainer container)
        where TImpl : TSvc
    {
        container.Register<TSvc, TImpl>(Reuse.Singleton);

        return container;
    }
    
    public static IContainer AddSingleton<TSvc, TImpl>(this IContainer container, TImpl obj)
        where TImpl : TSvc
    {
        container.RegisterInstance<TSvc>(obj);

        return container;
    }

    public static IContainer AddSingleton<TImpl>(this IContainer container)
    {
        container.Register<TImpl>(Reuse.Singleton);

        return container;
    }

    public static IContainer AddSingleton<TImpl>(this IContainer container, TImpl obj)
    {
        container.RegisterInstance<TImpl>(obj);

        return container;
    }
    
    public static IContainer AddSingleton<TImpl>(this IContainer container, Func<IResolverContext, TImpl> factory)
    {
        container.RegisterDelegate(factory);

        return container;
    }
}