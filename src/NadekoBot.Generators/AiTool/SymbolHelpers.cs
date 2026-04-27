using Microsoft.CodeAnalysis;

namespace NadekoBot.Generators;

internal static class SymbolHelpers
{
    public static bool ImplementsInterface(INamedTypeSymbol type, string interfaceFqn)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ToDisplayString() == interfaceFqn)
                return true;
        }
        return false;
    }

    public static AttributeData? GetAttribute(ISymbol symbol, string attrFqn)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == attrFqn)
                return attr;
        }
        return null;
    }

    public static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return named.TypeArguments[0];
        }
        return type;
    }

    public static string[] GetEnumMemberNames(ITypeSymbol type)
    {
        var result = new List<string>();
        foreach (var m in type.GetMembers())
        {
            if (m is IFieldSymbol fs && fs.HasConstantValue)
                result.Add(fs.Name);
        }
        return result.ToArray();
    }
}
