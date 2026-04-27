using Microsoft.CodeAnalysis;

namespace NadekoBot.Generators;

/// <summary>
/// Captured shape of a single [AiTool]-attributed adapter method. Built once
/// during incremental extraction and kept immutable thereafter.
/// </summary>
internal sealed class ToolMethodInfo
{
    public string ToolName { get; }
    public string ToolDescription { get; }
    public string MethodName { get; }
    public string ContainingTypeFull { get; }
    public string ContainingTypeShort { get; }
    public string ContainingNamespace { get; }
    public IReadOnlyList<ToolParamInfo> Parameters { get; }
    public bool HasContext { get; }
    public bool InnerReturnTypeIsVoid { get; }
    public bool ReturnsString { get; }
    public IReadOnlyList<string> GuildPerms { get; }
    public IReadOnlyList<string> ChannelPerms { get; }
    public bool OwnerOnly { get; }
    public bool IsCoreTool { get; }
    public string? SystemGuidance { get; }

    /// <summary>
    /// True when the generated ExecuteAsync needs `await` somewhere -- i.e. the
    /// adapter returns a non-string typed task we must serialise after awaiting.
    /// When false (Task&lt;string&gt; passthrough), we can drop the async keyword and
    /// avoid the state-machine allocation per call.
    /// </summary>
    public bool HasAwaitableBody => !ReturnsString;

    private ToolMethodInfo(
        string toolName,
        string toolDescription,
        string methodName,
        string containingTypeFull,
        string containingTypeShort,
        string containingNamespace,
        IReadOnlyList<ToolParamInfo> parameters,
        bool hasContext,
        bool innerReturnTypeIsVoid,
        bool returnsString,
        IReadOnlyList<string> guildPerms,
        IReadOnlyList<string> channelPerms,
        bool ownerOnly,
        bool isCoreTool,
        string? systemGuidance)
    {
        ToolName = toolName;
        ToolDescription = toolDescription;
        MethodName = methodName;
        ContainingTypeFull = containingTypeFull;
        ContainingTypeShort = containingTypeShort;
        ContainingNamespace = containingNamespace;
        Parameters = parameters;
        HasContext = hasContext;
        InnerReturnTypeIsVoid = innerReturnTypeIsVoid;
        ReturnsString = returnsString;
        GuildPerms = guildPerms;
        ChannelPerms = channelPerms;
        OwnerOnly = ownerOnly;
        IsCoreTool = isCoreTool;
        SystemGuidance = systemGuidance;
    }

    public static ToolMethodInfo? From(GeneratorAttributeSyntaxContext ctx)
    {
        var method = (IMethodSymbol)ctx.TargetSymbol;
        var containingType = method.ContainingType;

        if (!SymbolHelpers.ImplementsInterface(containingType, "NadekoBot.AiAgent.IAiToolGroup"))
            return null;

        var aiToolAttr = SymbolHelpers.GetAttribute(method, "NadekoBot.AiAgent.AiToolAttribute");
        if (aiToolAttr is null || aiToolAttr.ConstructorArguments.Length < 2)
            return null;

        var toolName = aiToolAttr.ConstructorArguments[0].Value as string ?? "";
        var toolDesc = aiToolAttr.ConstructorArguments[1].Value as string ?? "";

        var isCoreTool = SymbolHelpers.ImplementsInterface(containingType, "NadekoBot.AiAgent.IAiCoreToolGroup");

        var systemGuidanceAttr = SymbolHelpers.GetAttribute(method, "NadekoBot.AiAgent.AiSystemGuidanceAttribute");
        var systemGuidance = systemGuidanceAttr is { ConstructorArguments.Length: > 0 }
            ? systemGuidanceAttr.ConstructorArguments[0].Value as string
            : null;

        var parameters = new List<ToolParamInfo>(method.Parameters.Length);
        var hasContext = false;

        foreach (var paramSymbol in method.Parameters)
        {
            var typeFull = paramSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (typeFull == "global::NadekoBot.Modules.Utility.AiAgent.AiToolContext"
                || typeFull == "NadekoBot.Modules.Utility.AiAgent.AiToolContext")
            {
                hasContext = true;
                continue;
            }

            parameters.Add(ToolParamInfo.From(paramSymbol));
        }

        var guildPerms = new List<string>();
        var channelPerms = new List<string>();
        var ownerOnly = false;

        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            switch (name)
            {
                case "NadekoBot.AiAgent.AiRequiresPermAttribute" when attr.ConstructorArguments.Length > 0:
                    if (attr.ConstructorArguments[0].Value is { } gp)
                        guildPerms.Add(gp.ToString());
                    break;
                case "NadekoBot.AiAgent.AiRequiresChannelPermAttribute" when attr.ConstructorArguments.Length > 0:
                    if (attr.ConstructorArguments[0].Value is { } cp)
                        channelPerms.Add(cp.ToString());
                    break;
                case "NadekoBot.AiAgent.AiOwnerOnlyAttribute":
                    ownerOnly = true;
                    break;
            }
        }

        var innerVoid = true;
        var returnsString = false;
        if (method.ReturnType is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
        {
            innerVoid = false;
            returnsString = named.TypeArguments[0].SpecialType == SpecialType.System_String;
        }

        return new ToolMethodInfo(
            toolName,
            toolDesc,
            method.Name,
            containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            containingType.Name,
            containingType.ContainingNamespace.ToDisplayString(),
            parameters,
            hasContext,
            innerVoid,
            returnsString,
            guildPerms,
            channelPerms,
            ownerOnly,
            isCoreTool,
            systemGuidance);
    }
}
