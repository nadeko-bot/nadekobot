using DryIoc;
using System.Reflection;
using System.Text.Json;

namespace NadekoBot.Medusa;

public interface IIocModule
{
    public string Name { get; }
    public void Load();
    public void Unload();
}

public sealed class MedusaNinjectIocModule : IIocModule, IDisposable
{
    public string Name { get; }
    private volatile bool isLoaded = false;
    private readonly Dictionary<Type, Type[]> _types;
    private readonly IContainer _cont;

    public MedusaNinjectIocModule(IContainer cont, Assembly assembly, string name)
    {
        Name = name;
        _cont = cont;
        _types = assembly.GetExportedTypes()
            .Where(t => t.IsClass)
            .Where(t => t.GetCustomAttribute<svcAttribute>() is not null)
            .ToDictionary(x => x,
                type => type.GetInterfaces().ToArray());
    }

    public void Load()
    {
        if (isLoaded)
            return;

        foreach (var (type, _) in _types)
        {
            var attribute = type.GetCustomAttribute<svcAttribute>()!;

            var reuse = attribute.Lifetime == Lifetime.Singleton
                ? Reuse.Singleton
                : Reuse.Transient;
            
            _cont.RegisterMany([type], reuse);
        }

        isLoaded = true;
    }

    public void Unload()
    {
        if (!isLoaded)
            return;
        
        foreach (var type in _types.Keys)
        {
            _cont.Unregister(type);
        }
        
        _types.Clear();
        
        // in case the library uses System.Text.Json
        var assembly = typeof(JsonSerializerOptions).Assembly;
        var updateHandlerType = assembly.GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler");
        var clearCacheMethod = updateHandlerType?.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.Public);
        clearCacheMethod?.Invoke(null, [null]); 
        
        isLoaded = false;
    }

    public void Dispose()
        => _types.Clear();
}