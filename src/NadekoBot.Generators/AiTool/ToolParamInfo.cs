using Microsoft.CodeAnalysis;

namespace NadekoBot.Generators;

/// <summary>
/// What kind of value a tool parameter represents. Drives both schema emission
/// (which JSON type/shape goes into the schema) and deserialization emission
/// (which TryGetProperty / Deserialize calls go into the generated code).
/// </summary>
internal enum ParamKind
{
    /// <summary>String. JSON `"type":"string"`.</summary>
    String,
    /// <summary>UInt64 / Discord snowflake. Encoded as JSON string.</summary>
    Ulong,
    /// <summary>Int32. JSON `"type":"integer"`.</summary>
    Int,
    /// <summary>Int64. JSON `"type":"integer"`.</summary>
    Long,
    /// <summary>Double / float / decimal. JSON `"type":"number"`.</summary>
    Double,
    /// <summary>Boolean. JSON `"type":"boolean"`.</summary>
    Bool,
    /// <summary>Enum. JSON `"type":"string"` with `enum` constraint.</summary>
    Enum,
    /// <summary>Pass-through JsonElement (no deserialization).</summary>
    JsonElement,
    /// <summary>Record / class / List&lt;T&gt; / array etc. Deserialised via System.Text.Json.</summary>
    Object,
}

/// <summary>
/// Captured shape of a single adapter method parameter (excluding AiToolContext,
/// which the generator passes through implicitly).
/// </summary>
internal sealed class ToolParamInfo
{
    public string Name { get; }
    public string TypeFull { get; }
    public string Description { get; }
    public bool IsOptional { get; }
    public bool HasDefault { get; }
    public object? DefaultValue { get; }
    public ParamKind Kind { get; }
    public IReadOnlyList<string> EnumMembers { get; }
    public ITypeSymbol Type { get; }

    private ToolParamInfo(
        string name,
        string typeFull,
        string description,
        bool isOptional,
        bool hasDefault,
        object? defaultValue,
        ParamKind kind,
        IReadOnlyList<string> enumMembers,
        ITypeSymbol type)
    {
        Name = name;
        TypeFull = typeFull;
        Description = description;
        IsOptional = isOptional;
        HasDefault = hasDefault;
        DefaultValue = defaultValue;
        Kind = kind;
        EnumMembers = enumMembers;
        Type = type;
    }

    public static ToolParamInfo From(IParameterSymbol param)
    {
        var paramAttr = SymbolHelpers.GetAttribute(param, "NadekoBot.AiAgent.AiParamAttribute");
        var desc = paramAttr is { ConstructorArguments.Length: > 0 }
            ? paramAttr.ConstructorArguments[0].Value as string ?? ""
            : "";

        var hasDefault = param.HasExplicitDefaultValue;
        var defaultValue = hasDefault ? param.ExplicitDefaultValue : null;

        var unwrapped = SymbolHelpers.UnwrapNullable(param.Type);
        var kind = ClassifyKind(unwrapped);
        var enumMembers = kind == ParamKind.Enum
            ? SymbolHelpers.GetEnumMemberNames(unwrapped)
            : Array.Empty<string>();

        var isOptional = param.IsOptional
                         || param.NullableAnnotation == NullableAnnotation.Annotated;

        return new ToolParamInfo(
            param.Name,
            param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            desc,
            isOptional,
            hasDefault,
            defaultValue,
            kind,
            enumMembers,
            unwrapped);
    }

    private static ParamKind ClassifyKind(ITypeSymbol unwrapped)
    {
        switch (unwrapped.SpecialType)
        {
            case SpecialType.System_String: return ParamKind.String;
            case SpecialType.System_UInt64: return ParamKind.Ulong;
            case SpecialType.System_Int32:
            case SpecialType.System_Int16:
            case SpecialType.System_Byte:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt16:
                return ParamKind.Int;
            case SpecialType.System_Int64: return ParamKind.Long;
            case SpecialType.System_Boolean: return ParamKind.Bool;
            case SpecialType.System_Double:
            case SpecialType.System_Single:
            case SpecialType.System_Decimal:
                return ParamKind.Double;
        }

        if (unwrapped.TypeKind == TypeKind.Enum)
            return ParamKind.Enum;

        var fqn = unwrapped.ToDisplayString();
        if (fqn == "System.Text.Json.JsonElement")
            return ParamKind.JsonElement;

        return ParamKind.Object;
    }
}
