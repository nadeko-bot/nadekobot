using System.Reflection;
using System.Runtime.Loader;

namespace NadekoBot.Medusa;

public class MedusaAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    
    public MedusaAssemblyLoadContext(string folderPath) : base(isCollectible: true)
        => _resolver = new(folderPath);

    // public Assembly MainAssembly { get; private set; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            var assembly = LoadFromAssemblyPath(assemblyPath);
            LoadDependencies(assembly);
            return assembly;
        }

        return null;
    }

    public void LoadDependencies(Assembly assembly)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            Load(reference);
        }
    }
}