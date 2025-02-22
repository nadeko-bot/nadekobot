using System.Reflection;

namespace NadekoBot.Medusa;

public sealed class SnekCommandData
{
    public SnekCommandData(
        IReadOnlyCollection<string> aliases,
        MethodInfo methodInfo,
        Snek module,
        FilterAttribute[] filters,
        MedusaPermAttribute[] userAndBotPerms,
        CommandContextType contextType,
        IReadOnlyList<Type> injectedParams,
        IReadOnlyList<ParamData> parameters,
        CommandStrings strings,
        int priority)
    {
        Aliases = aliases;
        MethodInfo = methodInfo;
        Module = module;
        Filters = filters;
        UserAndBotPerms = userAndBotPerms;
        ContextType = contextType;
        InjectedParams = injectedParams;
        Parameters = parameters;
        Priority = priority;
        OptionalStrings = strings;
    }

    public MedusaPermAttribute[] UserAndBotPerms { get; set; }

    public CommandStrings OptionalStrings { get; set; }

    public IReadOnlyCollection<string> Aliases { get; }
    public MethodInfo MethodInfo { get; set; }
    public Snek Module { get; set; }
    public FilterAttribute[] Filters { get; set; }
    public CommandContextType ContextType { get; }
    public IReadOnlyList<Type> InjectedParams { get; }
    public IReadOnlyList<ParamData> Parameters { get; }
    public int Priority { get; }
}