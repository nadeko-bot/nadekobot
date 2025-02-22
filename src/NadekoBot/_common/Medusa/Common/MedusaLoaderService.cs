using Discord.Commands.Builders;
using DryIoc;
using Microsoft.Extensions.DependencyInjection;
using Nadeko.Common.Medusa;
using Nadeko.Medusa.Adapters;
using NadekoBot.Common.ModuleBehaviors;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NadekoBot.Medusa;

// ReSharper disable RedundantAssignment
public sealed class MedusaLoaderService : IMedusaLoaderService, IReadyExecutor, INService
{
    private readonly CommandService _cmdService;
    private readonly IBehaviorHandler _behHandler;
    private readonly IPubSub _pubSub;
    private readonly IMedusaConfigService _medusaConfig;
    private readonly IContainer _cont;

    private readonly ConcurrentDictionary<string, ResolvedMedusa> _resolved = new();
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    private readonly TypedKey<string> _loadKey = new("medusa:load");
    private readonly TypedKey<string> _unloadKey = new("medusa:unload");

    private readonly TypedKey<bool> _stringsReload = new("medusa:reload_strings");

    private const string BASE_DIR = "data/medusae";

    public MedusaLoaderService(
        CommandService cmdService,
        IContainer cont,
        IBehaviorHandler behHandler,
        IPubSub pubSub,
        IMedusaConfigService medusaConfig)
    {
        _cmdService = cmdService;
        _behHandler = behHandler;
        _pubSub = pubSub;
        _medusaConfig = medusaConfig;
        _cont = cont;

        // has to be done this way to support this feature on sharded bots
        _pubSub.Sub(_loadKey, async name => await InternalLoadAsync(name));
        _pubSub.Sub(_unloadKey, async name => await InternalUnloadAsync(name));

        _pubSub.Sub(_stringsReload, async _ => await ReloadStringsInternal());
    }

    public IReadOnlyCollection<string> GetAllMedusae()
    {
        if (!Directory.Exists(BASE_DIR))
            return Array.Empty<string>();

        return Directory.GetDirectories(BASE_DIR)
                        .Select(x => Path.GetRelativePath(BASE_DIR, x))
                        .ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public IReadOnlyCollection<MedusaStats> GetLoadedMedusae(CultureInfo? culture)
    {
        var toReturn = new List<MedusaStats>(_resolved.Count);
        foreach (var (name, resolvedData) in _resolved)
        {
            var sneks = new List<SnekStats>(resolvedData.SnekInfos.Count);

            foreach (var snekInfos in resolvedData.SnekInfos.Concat(resolvedData.SnekInfos.SelectMany(x => x.Subsneks)))
            {
                var commands = new List<SnekCommandStats>();

                foreach (var command in snekInfos.Commands)
                {
                    commands.Add(new SnekCommandStats(command.Aliases.First()));
                }

                sneks.Add(new SnekStats(snekInfos.Name, snekInfos.Instance.Prefix, commands));
            }

            toReturn.Add(new MedusaStats(name, resolvedData.Strings.GetDescription(culture), sneks));
        }

        return toReturn;
    }

    public async Task OnReadyAsync()
    {
        foreach (var name in _medusaConfig.GetLoadedMedusae())
        {
            var result = await InternalLoadAsync(name);
            if (result != MedusaLoadResult.Success)
                Log.Warning("Unable to load '{MedusaName}' medusa", name);
            else
                Log.Warning("Loaded medusa '{MedusaName}'", name);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<MedusaLoadResult> LoadMedusaAsync(string medusaName)
    {
        // try loading on this shard first to see if it works
        var res = await InternalLoadAsync(medusaName);
        if (res == MedusaLoadResult.Success)
        {
            // if it does publish it so that other shards can load the medusa too
            // this method will be ran twice on this shard but it doesn't matter as 
            // the second attempt will be ignored
            await _pubSub.Pub(_loadKey, medusaName);
        }

        return res;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<MedusaUnloadResult> UnloadMedusaAsync(string medusaName)
    {
        var res = await InternalUnloadAsync(medusaName);
        if (res == MedusaUnloadResult.Success)
        {
            await _pubSub.Pub(_unloadKey, medusaName);
        }

        return res;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string[] GetCommandExampleArgs(string medusaName, string commandName, CultureInfo culture)
    {
        if (!_resolved.TryGetValue(medusaName, out var data))
            return Array.Empty<string>();

        return data.Strings.GetCommandStrings(commandName, culture).Args
               ?? data.SnekInfos
                      .SelectMany(x => x.Commands)
                      .FirstOrDefault(x => x.Aliases.Any(alias
                          => alias.Equals(commandName, StringComparison.InvariantCultureIgnoreCase)))
                      ?.OptionalStrings
                      .Args
               ?? [string.Empty];
    }

    public Task ReloadStrings()
        => _pubSub.Pub(_stringsReload, true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReloadStringsSync()
    {
        foreach (var resolved in _resolved.Values)
        {
            resolved.Strings.Reload();
        }
    }

    private async Task ReloadStringsInternal()
    {
        await _lock.WaitAsync();
        try
        {
            ReloadStringsSync();
        }
        finally
        {
            _lock.Release();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public string GetCommandDescription(string medusaName, string commandName, CultureInfo culture)
    {
        if (!_resolved.TryGetValue(medusaName, out var data))
            return string.Empty;

        return data.Strings.GetCommandStrings(commandName, culture).Desc
               ?? data.SnekInfos
                      .SelectMany(x => x.Commands)
                      .FirstOrDefault(x => x.Aliases.Any(alias
                          => alias.Equals(commandName, StringComparison.InvariantCultureIgnoreCase)))
                      ?.OptionalStrings
                      .Desc
               ?? string.Empty;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async ValueTask<MedusaLoadResult> InternalLoadAsync(string name)
    {
        if (_resolved.ContainsKey(name))
            return MedusaLoadResult.AlreadyLoaded;

        var safeName = Uri.EscapeDataString(name);

        await _lock.WaitAsync();
        try
        {
            if (LoadAssemblyInternal(safeName,
                    out var ctx,
                    out var snekData,
                    out var iocModule,
                    out var strings,
                    out var typeReaders))
            {
                var moduleInfos = new List<ModuleInfo>();

                LoadTypeReadersInternal(typeReaders);

                foreach (var point in snekData)
                {
                    try
                    {
                        // initialize snek and subsneks
                        await point.Instance.InitializeAsync();
                        foreach (var sub in point.Subsneks)
                        {
                            await sub.Instance.InitializeAsync();
                        }

                        var module = await LoadModuleInternalAsync(name, point, strings, iocModule);
                        moduleInfos.Add(module);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex,
                            "Error loading snek {SnekName}",
                            point.Name);
                    }
                }

                var execs = GetExecsInternal(snekData, strings);
                await _behHandler.AddRangeAsync(execs);

                _resolved[name] = new(LoadContext: ctx,
                    ModuleInfos: moduleInfos.ToImmutableArray(),
                    SnekInfos: snekData.ToImmutableArray(),
                    strings,
                    typeReaders,
                    execs)
                {
                    IocModule = iocModule
                };


                _medusaConfig.AddLoadedMedusa(safeName);
                return MedusaLoadResult.Success;
            }

            return MedusaLoadResult.Empty;
        }
        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException)
        {
            Log.Error(ex, "An error occurred loading a medusa");
            return MedusaLoadResult.NotFound;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred loading a medusa");
            return MedusaLoadResult.UnknownError;
        }
        finally
        {
            _lock.Release();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private IReadOnlyCollection<ICustomBehavior> GetExecsInternal(
        IReadOnlyCollection<SnekInfo> snekData,
        IMedusaStrings strings)
    {
        var behs = new List<ICustomBehavior>();
        foreach (var snek in snekData)
        {
            behs.Add(new BehaviorAdapter(new(snek.Instance), strings, _cont));

            foreach (var sub in snek.Subsneks)
            {
                behs.Add(new BehaviorAdapter(new(sub.Instance), strings, _cont));
            }
        }


        return behs;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void LoadTypeReadersInternal(Dictionary<Type, TypeReader> typeReaders)
    {
        var notAddedTypeReaders = new List<Type>();
        foreach (var (type, typeReader) in typeReaders)
        {
            // if type reader for this type already exists, it will not be replaced
            if (_cmdService.TypeReaders.Contains(type))
            {
                notAddedTypeReaders.Add(type);
                continue;
            }

            _cmdService.AddTypeReader(type, typeReader);
        }

        // remove the ones that were not added
        // to prevent them from being unloaded later
        // as they didn't come from this medusa
        foreach (var toRemove in notAddedTypeReaders)
        {
            typeReaders.Remove(toRemove);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool LoadAssemblyInternal(
        string safeName,
        [NotNullWhen(true)] out WeakReference<MedusaAssemblyLoadContext>? ctxWr,
        [NotNullWhen(true)] out IReadOnlyCollection<SnekInfo>? snekData,
        [NotNullWhen(true)] out IIocModule? iocModule,
        out IMedusaStrings strings,
        out Dictionary<Type, TypeReader> typeReaders)
    {
        ctxWr = null;
        snekData = null;

        var path = Path.GetFullPath($"{BASE_DIR}/{safeName}/{safeName}.dll");
        var dir = Path.GetFullPath($"{BASE_DIR}/{safeName}");

        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Medusa folder not found: {dir}");
        
        if (!File.Exists(path))
            throw new FileNotFoundException($"Medusa dll not found: {path}");

        strings = MedusaStrings.CreateDefault(dir);
        var ctx = new MedusaAssemblyLoadContext(path);
        var a = ctx.LoadFromAssemblyPath(Path.GetFullPath(path));
        // ctx.LoadDependencies(a);

        iocModule = null;
        // load services
        try
        {
            iocModule = new MedusaNinjectIocModule(_cont, a, safeName);
            iocModule.Load();

            var sis = LoadSneksFromAssembly(safeName, a);
            typeReaders = LoadTypeReadersFromAssembly(a, strings);

            if (sis.Count == 0)
            {
                iocModule.Unload();
                ctx.Unload();
                return false;
            }

            ctxWr = new(ctx);
            snekData = sis;

            return true;
        }
        catch
        {
            iocModule?.Unload();
            ctx.Unload();
            throw;
        }
    }

    private static readonly Type _paramParserType = typeof(ParamParser<>);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Dictionary<Type, TypeReader> LoadTypeReadersFromAssembly(
        Assembly assembly,
        IMedusaStrings strings)
    {
        var paramParsers = assembly.GetExportedTypes()
                                   .Where(x => x.IsClass
                                               && !x.IsAbstract
                                               && x.BaseType is not null
                                               && x.BaseType.IsGenericType
                                               && x.BaseType.GetGenericTypeDefinition() == _paramParserType);

        var typeReaders = new Dictionary<Type, TypeReader>();
        foreach (var parserType in paramParsers)
        {
            var parserObj = ActivatorUtilities.CreateInstance(_cont, parserType);

            var targetType = parserType.BaseType!.GetGenericArguments()[0];
            var typeReaderInstance = (TypeReader)Activator.CreateInstance(
                typeof(ParamParserAdapter<>).MakeGenericType(targetType),
                args: [parserObj, strings, _cont])!;

            typeReaders.Add(targetType, typeReaderInstance);
        }

        return typeReaders;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<ModuleInfo> LoadModuleInternalAsync(
        string medusaName,
        SnekInfo snekInfo,
        IMedusaStrings strings,
        IIocModule services)
    {
        var module = await _cmdService.CreateModuleAsync(snekInfo.Instance.Prefix,
            CreateModuleFactory(medusaName, snekInfo, strings, services));

        return module;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Action<ModuleBuilder> CreateModuleFactory(
        string medusaName,
        SnekInfo snekInfo,
        IMedusaStrings strings,
        IIocModule iocModule)
        => mb =>
        {
            var m = mb.WithName(snekInfo.Name);

            foreach (var f in snekInfo.Filters)
            {
                m.AddPrecondition(new FilterAdapter(f, strings));
            }

            foreach (var cmd in snekInfo.Commands)
            {
                m.AddCommand(cmd.Aliases.First(),
                    CreateCallback(cmd.ContextType,
                        new(snekInfo),
                        new(cmd),
                        strings),
                    CreateCommandFactory(medusaName, cmd, strings));
            }

            foreach (var subInfo in snekInfo.Subsneks)
                m.AddModule(subInfo.Instance.Prefix, CreateModuleFactory(medusaName, subInfo, strings, iocModule));
        };

    private static readonly RequireContextAttribute _reqGuild = new RequireContextAttribute(ContextType.Guild);
    private static readonly RequireContextAttribute _reqDm = new RequireContextAttribute(ContextType.DM);

    private Action<CommandBuilder> CreateCommandFactory(string medusaName, SnekCommandData cmd, IMedusaStrings strings)
        => (cb) =>
        {
            cb.AddAliases(cmd.Aliases.Skip(1).ToArray());

            if (cmd.ContextType == CommandContextType.Guild)
                cb.AddPrecondition(_reqGuild);
            else if (cmd.ContextType == CommandContextType.Dm)
                cb.AddPrecondition(_reqDm);

            foreach (var f in cmd.Filters)
                cb.AddPrecondition(new FilterAdapter(f, strings));

            foreach (var ubp in cmd.UserAndBotPerms)
            {
                if (ubp is user_permAttribute up)
                {
                    if (up.GuildPerm is { } gp)
                        cb.AddPrecondition(new UserPermAttribute(gp));
                    else if (up.ChannelPerm is { } cp)
                        cb.AddPrecondition(new UserPermAttribute(cp));
                }
                else if (ubp is bot_permAttribute bp)
                {
                    if (bp.GuildPerm is { } gp)
                        cb.AddPrecondition(new BotPermAttribute(gp));
                    else if (bp.ChannelPerm is { } cp)
                        cb.AddPrecondition(new BotPermAttribute(cp));
                }
                else if (ubp is bot_owner_onlyAttribute)
                {
                    cb.AddPrecondition(new OwnerOnlyAttribute());
                }
            }

            cb.WithPriority(cmd.Priority);

            // using summary to save method name
            // method name is used to retrieve desc/usages
            cb.WithRemarks($"medusa///{medusaName}");
            cb.WithSummary(cmd.MethodInfo.Name.ToLowerInvariant());

            foreach (var param in cmd.Parameters)
            {
                cb.AddParameter(param.Name, param.Type, CreateParamFactory(param));
            }
        };

    private Action<ParameterBuilder> CreateParamFactory(ParamData paramData)
        => (pb) =>
        {
            pb.WithIsMultiple(paramData.IsParams)
              .WithIsOptional(paramData.IsOptional)
              .WithIsRemainder(paramData.IsLeftover);

            if (paramData.IsOptional)
                pb.WithDefault(paramData.DefaultValue);
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Func<ICommandContext, object[], IServiceProvider, CommandInfo, Task> CreateCallback(
        CommandContextType contextType,
        WeakReference<SnekInfo> snekDataWr,
        WeakReference<SnekCommandData> snekCommandDataWr,
        IMedusaStrings strings)
        => async (
            context,
            parameters,
            svcs,
            _) =>
        {
            if (!snekCommandDataWr.TryGetTarget(out var cmdData)
                || !snekDataWr.TryGetTarget(out var snekData))
            {
                Log.Warning("Attempted to run an unloaded snek's command");
                return;
            }

            var paramObjs = ParamObjs(contextType, cmdData, parameters, context, svcs, _cont, strings);

            try
            {
                var methodInfo = cmdData.MethodInfo;
                if (methodInfo.ReturnType == typeof(Task)
                    || (methodInfo.ReturnType.IsGenericType
                        && methodInfo.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
                {
                    await (Task)methodInfo.Invoke(snekData.Instance, paramObjs)!;
                }
                else if (methodInfo.ReturnType == typeof(ValueTask))
                {
                    await ((ValueTask)methodInfo.Invoke(snekData.Instance, paramObjs)!).AsTask();
                }
                else // if (methodInfo.ReturnType == typeof(void))
                {
                    methodInfo.Invoke(snekData.Instance, paramObjs);
                }
            }
            finally
            {
                paramObjs = null;
                cmdData = null;

                snekData = null;
            }
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object[] ParamObjs(
        CommandContextType contextType,
        SnekCommandData cmdData,
        object[] parameters,
        ICommandContext context,
        IServiceProvider svcs,
        IServiceProvider svcProvider,
        IMedusaStrings strings)
    {
        var extraParams = contextType == CommandContextType.Unspecified ? 0 : 1;
        extraParams += cmdData.InjectedParams.Count;

        var paramObjs = new object[parameters.Length + extraParams];

        var startAt = 0;
        if (contextType != CommandContextType.Unspecified)
        {
            paramObjs[0] = ContextAdapterFactory.CreateNew(context, strings, svcs);

            startAt = 1;
        }

        for (var i = 0; i < cmdData.InjectedParams.Count; i++)
        {
            var svc = svcProvider.GetService(cmdData.InjectedParams[i]);
            if (svc is null)
            {
                throw new ArgumentException($"Cannot inject a service of type {cmdData.InjectedParams[i]}");
            }

            paramObjs[i + startAt] = svc;

            svc = null;
        }

        startAt += cmdData.InjectedParams.Count;

        for (var i = 0; i < parameters.Length; i++)
            paramObjs[startAt + i] = parameters[i];

        return paramObjs;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<MedusaUnloadResult> InternalUnloadAsync(string name)
    {
        if (!_resolved.Remove(name, out var lsi))
            return MedusaUnloadResult.NotLoaded;

        await _lock.WaitAsync();
        try
        {
            UnloadTypeReaders(lsi.TypeReaders);

            foreach (var mi in lsi.ModuleInfos)
            {
                await _cmdService.RemoveModuleAsync(mi);
            }

            await _behHandler.RemoveRangeAsync(lsi.Execs);

            await DisposeSnekInstances(lsi);

            var lc = lsi.LoadContext;
            var km = lsi.IocModule;
            
            lsi.IocModule.Unload();
            lsi.IocModule = null!;
           
            if (km is IDisposable d)
                d.Dispose();
            
            lsi = null;

            _medusaConfig.RemoveLoadedMedusa(name);
            return UnloadInternal(lc)
                ? MedusaUnloadResult.Success
                : MedusaUnloadResult.PossiblyUnable;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void UnloadTypeReaders(Dictionary<Type, TypeReader> valueTypeReaders)
    {
        foreach (var tr in valueTypeReaders)
        {
            _cmdService.TryRemoveTypeReader(tr.Key, false, out _);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task DisposeSnekInstances(ResolvedMedusa medusa)
    {
        foreach (var si in medusa.SnekInfos)
        {
            try
            {
                await si.Instance.DisposeAsync();
                foreach (var sub in si.Subsneks)
                {
                    await sub.Instance.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Failed cleanup of Snek {SnekName}. This medusa might not unload correctly",
                    si.Instance.Name);
            }
        }

        // medusae = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool UnloadInternal(WeakReference<MedusaAssemblyLoadContext> lsi)
    {
        UnloadContext(lsi);
        GcCleanup();

        return !lsi.TryGetTarget(out _);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UnloadContext(WeakReference<MedusaAssemblyLoadContext> lsiLoadContext)
    {
        if (lsiLoadContext.TryGetTarget(out var ctx))
        {
            ctx.Unload();
        }
    }

    private void GcCleanup()
    {
        // cleanup
        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.WaitForFullGCComplete();
            GC.Collect();
        }
    }

    private static readonly Type _snekType = typeof(Snek);

    // [MethodImpl(MethodImplOptions.NoInlining)]
    // private MedusaIoCKernelModule LoadMedusaServicesInternal(string name, Assembly a)
    //     => new MedusaIoCKernelModule(name, a);


    [MethodImpl(MethodImplOptions.NoInlining)]
    public IReadOnlyCollection<SnekInfo> LoadSneksFromAssembly(string name, Assembly a)
    {
        // find all types in teh assembly
        var types = a.GetExportedTypes();
        // snek is always a public non abstract class
        var classes = types.Where(static x => x.IsClass
                                              && (x.IsNestedPublic || x.IsPublic)
                                              && !x.IsAbstract
                                              && x.BaseType == _snekType
                                              && (x.DeclaringType is null || x.DeclaringType.IsAssignableTo(_snekType)))
                           .ToList();

        var topModules = new Dictionary<Type, SnekInfo>();

        foreach (var cl in classes)
        {
            if (cl.DeclaringType is not null)
                continue;

            // get module data, and add it to the topModules dictionary
            var module = GetModuleData(cl);
            topModules.Add(cl, module);
        }

        foreach (var c in classes)
        {
            if (c.DeclaringType is not Type dt)
                continue;

            // if there is no top level module which this module is a child of
            // just print a warning and skip it
            if (!topModules.TryGetValue(dt, out var parentData))
            {
                Log.Warning("Can't load submodule {SubName} because parent module {Name} does not exist",
                    c.Name,
                    dt.Name);
                continue;
            }

            GetModuleData(c, parentData);
        }

        return topModules.Values.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SnekInfo GetModuleData(Type type, SnekInfo? parentData = null)
    {
        var filters = type.GetCustomAttributes<FilterAttribute>(true)
                          .ToArray();

        var instance = (Snek)ActivatorUtilities.CreateInstance(_cont, type);

        var module = new SnekInfo(instance.Name,
            parentData,
            instance,
            GetCommands(instance, type),
            filters);

        if (parentData is not null)
            parentData.Subsneks.Add(module);

        return module;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private IReadOnlyCollection<SnekCommandData> GetCommands(Snek instance, Type type)
    {
        var methodInfos = type
                          .GetMethods(BindingFlags.Instance
                                      | BindingFlags.DeclaredOnly
                                      | BindingFlags.Public)
                          .Where(static x =>
                          {
                              if (x.GetCustomAttribute<cmdAttribute>(true) is null)
                                  return false;

                              if (x.ReturnType.IsGenericType)
                              {
                                  var genericType = x.ReturnType.GetGenericTypeDefinition();
                                  if (genericType == typeof(Task<>))
                                      return true;

                                  // if (genericType == typeof(ValueTask<>))
                                  //     return true;

                                  Log.Warning("Method {MethodName} has an invalid return type: {ReturnType}",
                                      x.Name,
                                      x.ReturnType);

                                  return false;
                              }

                              var succ = x.ReturnType == typeof(Task)
                                         || x.ReturnType == typeof(ValueTask)
                                         || x.ReturnType == typeof(void);

                              if (!succ)
                              {
                                  Log.Warning("Method {MethodName} has an invalid return type: {ReturnType}",
                                      x.Name,
                                      x.ReturnType);
                              }

                              return succ;
                          });


        var cmds = new List<SnekCommandData>();
        foreach (var method in methodInfos)
        {
            var filters = method.GetCustomAttributes<FilterAttribute>(true).ToArray();
            var userAndBotPerms = method.GetCustomAttributes<MedusaPermAttribute>(true)
                                        .ToArray();
            var prio = method.GetCustomAttribute<prioAttribute>(true)?.Priority ?? 0;

            var paramInfos = method.GetParameters();
            var cmdParams = new List<ParamData>();
            var diParams = new List<Type>();
            var cmdContext = CommandContextType.Unspecified;
            var canInject = false;
            for (var paramCounter = 0; paramCounter < paramInfos.Length; paramCounter++)
            {
                var pi = paramInfos[paramCounter];

                var paramName = pi.Name ?? "unnamed";
                var isContext = paramCounter == 0 && pi.ParameterType.IsAssignableTo(typeof(AnyContext));

                var leftoverAttribute = pi.GetCustomAttribute<leftoverAttribute>(true);
                var hasDefaultValue = pi.HasDefaultValue;
                var defaultValue = pi.DefaultValue;
                var isLeftover = leftoverAttribute != null;
                var isParams = pi.GetCustomAttribute<ParamArrayAttribute>() is not null;
                var paramType = pi.ParameterType;
                var isInjected = pi.GetCustomAttribute<injectAttribute>(true) is not null;

                if (isContext)
                {
                    if (hasDefaultValue || leftoverAttribute != null || isParams)
                        throw new ArgumentException(
                            "IContext parameter cannot be optional, leftover, constant or params. "
                            + GetErrorPath(method, pi));

                    if (paramCounter != 0)
                        throw new ArgumentException($"IContext parameter has to be first. {GetErrorPath(method, pi)}");

                    canInject = true;

                    if (paramType.IsAssignableTo(typeof(GuildContext)))
                        cmdContext = CommandContextType.Guild;
                    else if (paramType.IsAssignableTo(typeof(DmContext)))
                        cmdContext = CommandContextType.Dm;
                    else
                        cmdContext = CommandContextType.Any;

                    continue;
                }

                if (isInjected)
                {
                    if (!canInject && paramCounter != 0)
                        throw new ArgumentException($"Parameters marked as [Injected] have to come after IContext");

                    canInject = true;

                    diParams.Add(paramType);
                    continue;
                }

                canInject = false;                                                                                           

                if (isParams)
                {
                    if (hasDefaultValue)
                        throw new NotSupportedException("Params can't have const values at the moment. "
                                                        + GetErrorPath(method, pi));
                    // if it's params, it means it's an array, and i only need a parser for the actual type,
                    // as the parser will run on each array element, it can't be null
                    paramType = paramType.GetElementType()!;
                }

                // leftover can only be the last parameter.
                if (isLeftover && paramCounter != paramInfos.Length - 1)
                {
                    var path = GetErrorPath(method, pi);
                    Log.Error("Only one parameter can be marked [Leftover] and it has to be the last one. {Path} ",
                        path);
                    throw new ArgumentException("Leftover attribute error.");
                }

                cmdParams.Add(new ParamData(paramType, paramName, hasDefaultValue, defaultValue, isLeftover, isParams));
            }


            var cmdAttribute = method.GetCustomAttribute<cmdAttribute>(true)!;
            var aliases = cmdAttribute.Aliases;
            if (aliases.Length == 0)
                aliases = [method.Name.ToLowerInvariant()];

            cmds.Add(new(
                aliases,
                method,
                instance,
                filters,
                userAndBotPerms,
                cmdContext,
                diParams,
                cmdParams,
                new(cmdAttribute.desc, cmdAttribute.args),
                prio
            ));
        }

        return cmds;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private string GetErrorPath(MethodInfo m, System.Reflection.ParameterInfo pi)
        => $@"Module: {m.DeclaringType?.Name} 
Command: {m.Name}
ParamName: {pi.Name}
ParamType: {pi.ParameterType.Name}";
}