#nullable enable
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace NadekoBot.Generators;

[Generator]
public class AiToolGenerator : IIncrementalGenerator
{
    private const string AI_TOOL_ATTR = "NadekoBot.AiAgent.AiToolAttribute";
    private const string AI_PARAM_ATTR = "NadekoBot.AiAgent.AiParamAttribute";
    private const string AI_REQUIRES_PERM_ATTR = "NadekoBot.AiAgent.AiRequiresPermAttribute";
    private const string AI_REQUIRES_CHANNEL_PERM_ATTR = "NadekoBot.AiAgent.AiRequiresChannelPermAttribute";
    private const string AI_OWNER_ONLY_ATTR = "NadekoBot.AiAgent.AiOwnerOnlyAttribute";
    private const string AI_SAFE_MUTATION_ATTR = "NadekoBot.AiAgent.AiSafeMutationAttribute";
    private const string AI_TOOL_CONTEXT_TYPE = "NadekoBot.Modules.Utility.AiAgent.AiToolContext";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
            AI_TOOL_ATTR,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => ExtractToolMethod(ctx));

        var collected = methods.Where(static m => m is not null).Collect();

        context.RegisterSourceOutput(collected, static (spc, methods) => Execute(spc, methods!));
    }

    private static ToolMethodInfo? ExtractToolMethod(GeneratorAttributeSyntaxContext ctx)
    {
        var methodSymbol = (IMethodSymbol)ctx.TargetSymbol;
        var containingType = methodSymbol.ContainingType;

        if (!ImplementsInterface(containingType, "NadekoBot.AiAgent.IAiToolGroup"))
            return null;

        var aiToolAttr = GetAttribute(methodSymbol, AI_TOOL_ATTR);
        if (aiToolAttr is null)
            return null;

        var toolName = aiToolAttr.ConstructorArguments[0].Value as string ?? "";
        var toolDesc = aiToolAttr.ConstructorArguments[1].Value as string ?? "";

        var parameters = new List<ToolParamInfo>();
        var hasContext = false;

        foreach (var param in methodSymbol.Parameters)
        {
            var paramTypeFull = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (paramTypeFull == "global::" + AI_TOOL_CONTEXT_TYPE
                || paramTypeFull == AI_TOOL_CONTEXT_TYPE)
            {
                hasContext = true;
                continue;
            }

            var aiParamAttr = GetAttribute(param, AI_PARAM_ATTR);
            var desc = aiParamAttr?.ConstructorArguments[0].Value as string ?? "";

            var hasDefault = param.HasExplicitDefaultValue;
            var defaultValue = hasDefault ? param.ExplicitDefaultValue : null;

            var paramTypeShort = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            parameters.Add(new ToolParamInfo(
                param.Name,
                paramTypeFull,
                paramTypeShort,
                desc,
                param.IsOptional || param.NullableAnnotation == NullableAnnotation.Annotated,
                hasDefault,
                defaultValue,
                param.Type.TypeKind == TypeKind.Enum,
                param.Type.TypeKind == TypeKind.Enum
                    ? GetEnumMembers(param.Type)
                    : Array.Empty<string>()));
        }

        var guildPerms = new List<string>();
        var channelPerms = new List<string>();
        var ownerOnly = false;

        foreach (var attr in methodSymbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName == AI_REQUIRES_PERM_ATTR && attr.ConstructorArguments.Length > 0)
            {
                var val = attr.ConstructorArguments[0].Value;
                if (val is not null)
                    guildPerms.Add(val.ToString());
            }
            else if (attrName == AI_REQUIRES_CHANNEL_PERM_ATTR && attr.ConstructorArguments.Length > 0)
            {
                var val = attr.ConstructorArguments[0].Value;
                if (val is not null)
                    channelPerms.Add(val.ToString());
            }
            else if (attrName == AI_OWNER_ONLY_ATTR)
            {
                ownerOnly = true;
            }
        }

        var returnTypeFull = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var innerReturnType = "void";
        if (methodSymbol.ReturnType is INamedTypeSymbol namedReturn
            && namedReturn.IsGenericType
            && namedReturn.TypeArguments.Length == 1)
        {
            innerReturnType = namedReturn.TypeArguments[0]
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var containingTypeFull = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var containingTypeShort = containingType.Name;
        var containingNamespace = containingType.ContainingNamespace.ToDisplayString();

        return new ToolMethodInfo(
            toolName,
            toolDesc,
            methodSymbol.Name,
            containingTypeFull,
            containingTypeShort,
            containingNamespace,
            parameters,
            hasContext,
            returnTypeFull,
            innerReturnType,
            guildPerms,
            channelPerms,
            ownerOnly);
    }

    private static void Execute(SourceProductionContext spc, ImmutableArray<ToolMethodInfo> methods)
    {
        if (methods.IsDefaultOrEmpty)
            return;

        foreach (var method in methods)
            GenerateToolClass(spc, method);
    }

    private static void GenerateToolClass(SourceProductionContext spc, ToolMethodInfo method)
    {
        var className = $"{method.ContainingTypeShort}_{method.MethodName}_AiTool";
        var sb = new StringBuilder();

        using (var stringWriter = new StringWriter(sb))
        using (var w = new IndentedTextWriter(stringWriter))
        {
            w.WriteLine("#pragma warning disable CS8600, CS8601, CS8602, CS8604");
            w.WriteLine("using System.Text.Json;");
            w.WriteLine("using NadekoBot.Modules.Utility.AiAgent;");
            w.WriteLine("using NadekoBot.Services;");
            w.WriteLine();
            w.WriteLine($"namespace {method.ContainingNamespace};");
            w.WriteLine();
            w.WriteLine($"public sealed class {className}({method.ContainingTypeFull} _adapter) : global::NadekoBot.Modules.Utility.AiAgent.IAiTool, global::NadekoBot.Services.INService");
            w.WriteLine("{");
            w.Indent++;

            w.WriteLine($"public string Name => \"{EscapeString(method.ToolName)}\";");
            w.WriteLine();
            w.WriteLine($"public string Description => \"{EscapeString(method.ToolDescription)}\";");
            w.WriteLine();

            w.WriteLine("public bool IsDataTool => true;");
            w.WriteLine();

            WriteParameterSchema(w, method);
            w.WriteLine();

            WriteExecuteMethod(w, method);

            w.Indent--;
            w.WriteLine("}");

            w.Flush();
        }

        spc.AddSource($"{className}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void WriteParameterSchema(IndentedTextWriter w, ToolMethodInfo method)
    {
        var schemaSb = new StringBuilder();
        schemaSb.Append("{");
        schemaSb.Append("\"type\":\"object\",");
        schemaSb.Append("\"properties\":{");

        var required = new List<string>();
        var first = true;

        foreach (var p in method.Parameters)
        {
            if (!first) schemaSb.Append(",");
            first = false;

            schemaSb.Append($"\"{p.Name}\":");
            schemaSb.Append("{");

            var jsonType = GetJsonType(p);
            schemaSb.Append($"\"type\":\"{jsonType}\"");

            if (p.IsEnum && p.EnumMembers.Length > 0)
            {
                schemaSb.Append(",\"enum\":[");
                for (var i = 0; i < p.EnumMembers.Length; i++)
                {
                    if (i > 0) schemaSb.Append(",");
                    schemaSb.Append($"\"{p.EnumMembers[i]}\"");
                }
                schemaSb.Append("]");
            }

            if (!string.IsNullOrEmpty(p.Description))
                schemaSb.Append($",\"description\":\"{EscapeString(p.Description)}\"");

            schemaSb.Append("}");

            if (!p.IsOptional)
                required.Add(p.Name);
        }

        schemaSb.Append("}");

        if (required.Count > 0)
        {
            schemaSb.Append(",\"required\":[");
            for (var i = 0; i < required.Count; i++)
            {
                if (i > 0) schemaSb.Append(",");
                schemaSb.Append($"\"{required[i]}\"");
            }
            schemaSb.Append("]");
        }

        schemaSb.Append("}");

        w.WriteLine($"public JsonElement ParameterSchema {{ get; }} = JsonDocument.Parse(\"{EscapeForVerbatim(schemaSb.ToString())}\").RootElement.Clone();");
    }

    private static void WriteExecuteMethod(IndentedTextWriter w, ToolMethodInfo method)
    {
        w.WriteLine("public async Task<string> ExecuteAsync(AiToolContext _ctx, JsonElement _args)");
        w.WriteLine("{");
        w.Indent++;

        if (method.OwnerOnly)
        {
            w.WriteLine("if (_ctx.User.Guild.OwnerId != _ctx.User.Id)");
            w.Indent++;
            w.WriteLine("return \"{\\\"error\\\":\\\"missing_permission\\\",\\\"required\\\":\\\"ServerOwner\\\"}\";");
            w.Indent--;
        }

        foreach (var perm in method.GuildPerms)
        {
            w.WriteLine($"if (!_ctx.User.GuildPermissions.Has((global::Discord.GuildPermission){perm}))");
            w.Indent++;
            w.WriteLine($"return \"{{\\\"error\\\":\\\"missing_permission\\\",\\\"required\\\":\\\"{GetPermName(perm)}\\\"}}\";" );
            w.Indent--;
        }

        foreach (var perm in method.ChannelPerms)
        {
            w.WriteLine($"if (!_ctx.User.GetPermissions((global::Discord.IGuildChannel)_ctx.SourceChannel).Has((global::Discord.ChannelPermission){perm}))");
            w.Indent++;
            w.WriteLine($"return \"{{\\\"error\\\":\\\"missing_permission\\\",\\\"required\\\":\\\"{GetPermName(perm)}\\\"}}\";" );
            w.Indent--;
        }

        foreach (var p in method.Parameters)
        {
            WriteParamDeserialization(w, p);
        }

        var adapterArgs = new StringBuilder();
        if (method.HasContext)
            adapterArgs.Append("_ctx");

        foreach (var p in method.Parameters)
        {
            if (adapterArgs.Length > 0) adapterArgs.Append(", ");
            adapterArgs.Append($"_p_{p.Name}");
        }

        if (method.InnerReturnType == "void")
        {
            w.WriteLine($"await _adapter.{method.MethodName}({adapterArgs});");
            w.WriteLine("return \"{\\\"ok\\\":true}\";");
        }
        else
        {
            w.WriteLine($"var _result = await _adapter.{method.MethodName}({adapterArgs});");
            w.WriteLine("return System.Text.Json.JsonSerializer.Serialize(_result);");
        }

        w.Indent--;
        w.WriteLine("}");
    }

    private static void WriteParamDeserialization(IndentedTextWriter w, ToolParamInfo p)
    {
        var varName = $"_p_{p.Name}";

        if (p.TypeFull.Contains("System.UInt64") || p.TypeShort == "ulong")
        {
            if (p.IsOptional)
            {
                w.WriteLine($"ulong? {varName} = null;");
                w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.ValueKind != JsonValueKind.Null)");
                w.WriteLine("{");
                w.Indent++;
                w.WriteLine($"if (ulong.TryParse(_el_{p.Name}.GetString() ?? _el_{p.Name}.GetRawText(), out var _parsed_{p.Name}))");
                w.Indent++;
                w.WriteLine($"{varName} = _parsed_{p.Name};");
                w.Indent--;
                w.Indent--;
                w.WriteLine("}");
            }
            else
            {
                w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name})");
                w.WriteLine($"    || !ulong.TryParse(_el_{p.Name}.GetString() ?? _el_{p.Name}.GetRawText(), out var {varName}))");
                w.Indent++;
                w.WriteLine($"return \"Error: {p.Name} is required and must be a valid ID.\";");
                w.Indent--;
            }
        }
        else if (p.TypeFull.Contains("System.Int32") || p.TypeShort == "int")
        {
            if (p.IsOptional)
            {
                var defaultStr = p.HasDefault && p.DefaultValue is not null ? p.DefaultValue.ToString() : "0";
                w.WriteLine($"var {varName} = {defaultStr};");
                w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.TryGetInt32(out var _parsed_{p.Name}))");
                w.Indent++;
                w.WriteLine($"{varName} = _parsed_{p.Name};");
                w.Indent--;
            }
            else
            {
                w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) || !_el_{p.Name}.TryGetInt32(out var {varName}))");
                w.Indent++;
                w.WriteLine($"return \"Error: {p.Name} is required and must be an integer.\";");
                w.Indent--;
            }
        }
        else if (p.TypeFull.Contains("System.Int64") || p.TypeShort == "long")
        {
            if (p.IsOptional)
            {
                var defaultStr = p.HasDefault && p.DefaultValue is not null ? p.DefaultValue.ToString() : "0";
                w.WriteLine($"var {varName} = {defaultStr}L;");
                w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.TryGetInt64(out var _parsed_{p.Name}))");
                w.Indent++;
                w.WriteLine($"{varName} = _parsed_{p.Name};");
                w.Indent--;
            }
            else
            {
                w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) || !_el_{p.Name}.TryGetInt64(out var {varName}))");
                w.Indent++;
                w.WriteLine($"return \"Error: {p.Name} is required and must be a number.\";");
                w.Indent--;
            }
        }
        else if (p.TypeFull.Contains("System.Boolean") || p.TypeShort == "bool")
        {
            var defaultStr = p.HasDefault && p.DefaultValue is bool bv ? (bv ? "true" : "false") : "false";
            w.WriteLine($"var {varName} = {defaultStr};");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.ValueKind is JsonValueKind.True or JsonValueKind.False)");
            w.Indent++;
            w.WriteLine($"{varName} = _el_{p.Name}.GetBoolean();");
            w.Indent--;
        }
        else if (p.TypeFull.Contains("System.String") || p.TypeShort == "string")
        {
            if (p.IsOptional)
            {
                var defaultStr = p.HasDefault && p.DefaultValue is string sv
                    ? $"\"{EscapeString(sv)}\""
                    : "null";
                w.WriteLine($"var {varName} = {defaultStr};");
                w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.ValueKind == JsonValueKind.String)");
                w.Indent++;
                w.WriteLine($"{varName} = _el_{p.Name}.GetString();");
                w.Indent--;
            }
            else
            {
                w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) || _el_{p.Name}.ValueKind != JsonValueKind.String)");
                w.Indent++;
                w.WriteLine($"return \"Error: {p.Name} is required.\";");
                w.Indent--;
                w.WriteLine($"var {varName} = _el_{p.Name}.GetString()!;");
            }
        }
        else if (p.IsEnum)
        {
            if (p.IsOptional)
            {
                w.WriteLine($"{p.TypeFull}? {varName} = null;");
                w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.ValueKind == JsonValueKind.String)");
                w.WriteLine("{");
                w.Indent++;
                w.WriteLine($"if (System.Enum.TryParse<{p.TypeFull}>(_el_{p.Name}.GetString(), true, out var _parsed_{p.Name}))");
                w.Indent++;
                w.WriteLine($"{varName} = _parsed_{p.Name};");
                w.Indent--;
                w.Indent--;
                w.WriteLine("}");
            }
            else
            {
                w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) || _el_{p.Name}.ValueKind != JsonValueKind.String");
                w.WriteLine($"    || !System.Enum.TryParse<{p.TypeFull}>(_el_{p.Name}.GetString(), true, out var {varName}))");
                w.Indent++;
                w.WriteLine($"return \"Error: {p.Name} is required and must be one of: {string.Join(", ", p.EnumMembers)}.\";");
                w.Indent--;
            }
        }
        else if (p.TypeFull.Contains("System.Double") || p.TypeShort == "double")
        {
            var defaultStr = p.HasDefault && p.DefaultValue is not null ? p.DefaultValue + "d" : "0d";
            w.WriteLine($"var {varName} = {defaultStr};");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}) && _el_{p.Name}.TryGetDouble(out var _parsed_{p.Name}))");
            w.Indent++;
            w.WriteLine($"{varName} = _parsed_{p.Name};");
            w.Indent--;
        }
        else
        {
            w.WriteLine($"var {varName} = default({p.TypeFull});");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var _el_{p.Name}))");
            w.Indent++;
            w.WriteLine($"{varName} = System.Text.Json.JsonSerializer.Deserialize<{p.TypeFull}>(_el_{p.Name}.GetRawText());");
            w.Indent--;
        }
    }

    private static string GetJsonType(ToolParamInfo p)
    {
        var t = p.TypeShort.TrimEnd('?');
        switch (t)
        {
            case "ulong":
            case "string":
                return "string";
            case "int":
            case "long":
                return "integer";
            case "double":
            case "float":
            case "decimal":
                return "number";
            case "bool":
                return "boolean";
            default:
                if (p.IsEnum) return "string";
                return "object";
        }
    }

    private static string GetPermName(string enumValue)
    {
        if (long.TryParse(enumValue, out _))
            return enumValue;
        return enumValue;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceFqn)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == interfaceFqn)
                return true;
        }
        return false;
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string attrFqn)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == attrFqn)
                return attr;
        }
        return null;
    }

    private static string[] GetEnumMembers(ITypeSymbol type)
    {
        var members = type.GetMembers();
        var result = new List<string>();
        foreach (var m in members)
        {
            if (m is IFieldSymbol fs && fs.HasConstantValue)
                result.Add(fs.Name);
        }
        return result.ToArray();
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static string EscapeForVerbatim(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

internal sealed class ToolMethodInfo
{
    public string ToolName { get; }
    public string ToolDescription { get; }
    public string MethodName { get; }
    public string ContainingTypeFull { get; }
    public string ContainingTypeShort { get; }
    public string ContainingNamespace { get; }
    public List<ToolParamInfo> Parameters { get; }
    public bool HasContext { get; }
    public string ReturnTypeFull { get; }
    public string InnerReturnType { get; }
    public List<string> GuildPerms { get; }
    public List<string> ChannelPerms { get; }
    public bool OwnerOnly { get; }

    public ToolMethodInfo(
        string toolName,
        string toolDescription,
        string methodName,
        string containingTypeFull,
        string containingTypeShort,
        string containingNamespace,
        List<ToolParamInfo> parameters,
        bool hasContext,
        string returnTypeFull,
        string innerReturnType,
        List<string> guildPerms,
        List<string> channelPerms,
        bool ownerOnly)
    {
        ToolName = toolName;
        ToolDescription = toolDescription;
        MethodName = methodName;
        ContainingTypeFull = containingTypeFull;
        ContainingTypeShort = containingTypeShort;
        ContainingNamespace = containingNamespace;
        Parameters = parameters;
        HasContext = hasContext;
        ReturnTypeFull = returnTypeFull;
        InnerReturnType = innerReturnType;
        GuildPerms = guildPerms;
        ChannelPerms = channelPerms;
        OwnerOnly = ownerOnly;
    }
}

internal sealed class ToolParamInfo
{
    public string Name { get; }
    public string TypeFull { get; }
    public string TypeShort { get; }
    public string Description { get; }
    public bool IsOptional { get; }
    public bool HasDefault { get; }
    public object? DefaultValue { get; }
    public bool IsEnum { get; }
    public string[] EnumMembers { get; }

    public ToolParamInfo(
        string name,
        string typeFull,
        string typeShort,
        string description,
        bool isOptional,
        bool hasDefault,
        object? defaultValue,
        bool isEnum,
        string[] enumMembers)
    {
        Name = name;
        TypeFull = typeFull;
        TypeShort = typeShort;
        Description = description;
        IsOptional = isOptional;
        HasDefault = hasDefault;
        DefaultValue = defaultValue;
        IsEnum = isEnum;
        EnumMembers = enumMembers;
    }
}
