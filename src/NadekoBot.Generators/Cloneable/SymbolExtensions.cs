// Code temporarily yeeted from
// https://github.com/mostmand/Cloneable/blob/master/Cloneable/CloneableGenerator.cs
// because of NRT issue

using Microsoft.CodeAnalysis;

namespace Cloneable
{
    internal static class SymbolExtensions
    {
        public static bool TryGetAttribute(this ISymbol symbol, INamedTypeSymbol attributeType,
            out IEnumerable<AttributeData> attributes)
        {
            attributes = symbol.GetAttributes()
                .Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
            return attributes.Any();
        }

        public static bool HasAttribute(this ISymbol symbol, INamedTypeSymbol attributeType)
            => symbol.GetAttributes()
                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
    }
}