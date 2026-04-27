using System.Text;
using Microsoft.CodeAnalysis;

namespace NadekoBot.Generators;

/// <summary>
/// Builds a JsonSchema tree for a tool's parameter object, recursively walking
/// record / class / list / array / enum types. The output is the JSON object
/// the LLM sees as the function's parameter schema.
/// </summary>
internal static class SchemaBuilder
{
    public static string BuildToolSchema(ToolMethodInfo method)
    {
        var properties = new List<(string Name, JsonSchema Schema)>(method.Parameters.Count);
        var required = new List<string>(method.Parameters.Count);

        foreach (var p in method.Parameters)
        {
            var schema = BuildForParam(p);
            properties.Add((p.Name, schema));
            if (!p.IsOptional)
                required.Add(p.Name);
        }

        return new ObjectSchema(properties, required).ToJson();
    }

    private static JsonSchema BuildForParam(ToolParamInfo p)
    {
        var inner = BuildForType(p.Type, p.Kind, p.EnumMembers);
        if (!string.IsNullOrEmpty(p.Description))
            inner = WithDescription(inner, p.Description);
        return inner;
    }

    private static JsonSchema BuildForType(ITypeSymbol type, ParamKind kind, IReadOnlyList<string> enumMembers)
    {
        switch (kind)
        {
            case ParamKind.String: return new PrimitiveSchema("string");
            case ParamKind.Ulong: return new PrimitiveSchema("string");
            case ParamKind.Int: return new PrimitiveSchema("integer");
            case ParamKind.Long: return new PrimitiveSchema("integer");
            case ParamKind.Double: return new PrimitiveSchema("number");
            case ParamKind.Bool: return new PrimitiveSchema("boolean");
            case ParamKind.Enum: return new EnumSchema(enumMembers);
            case ParamKind.JsonElement: return new PrimitiveSchema("object");
            case ParamKind.Object:
                return BuildForObjectType(type);
        }
        return new PrimitiveSchema("object");
    }

    private static JsonSchema BuildForObjectType(ITypeSymbol type)
    {
        // Date types -> string with format
        var fqn = type.ToDisplayString();
        if (fqn is "System.DateTime" or "System.DateTimeOffset" or "System.Guid")
            return fqn == "System.Guid"
                ? new PrimitiveSchema("string")
                : new PrimitiveSchema("string", "date-time");

        // Collection types -> array
        var elementType = GetCollectionElementType(type);
        if (elementType is not null)
        {
            var elementUnwrapped = SymbolHelpers.UnwrapNullable(elementType);
            var elementKind = ClassifyKind(elementUnwrapped);
            var elementMembers = elementKind == ParamKind.Enum
                ? SymbolHelpers.GetEnumMemberNames(elementUnwrapped)
                : Array.Empty<string>();
            var itemsSchema = BuildForType(elementUnwrapped, elementKind, elementMembers);
            return new ArraySchema(itemsSchema);
        }

        // Record / class / struct -> object with properties
        if (type is INamedTypeSymbol named && IsObjectShape(named))
        {
            var props = new List<(string, JsonSchema)>();
            var required = new List<string>();

            foreach (var member in EnumeratePublicProperties(named))
            {
                var propName = ToSnakeCase(member.Name);
                var memberType = member.Type;
                var unwrapped = SymbolHelpers.UnwrapNullable(memberType);
                var kind = ClassifyKind(unwrapped);
                var enumMembers = kind == ParamKind.Enum
                    ? SymbolHelpers.GetEnumMemberNames(unwrapped)
                    : Array.Empty<string>();

                var schema = BuildForType(unwrapped, kind, enumMembers);

                // [property: AiParam("...")] description
                var attr = SymbolHelpers.GetAttribute(member, "NadekoBot.AiAgent.AiParamAttribute");
                if (attr is { ConstructorArguments.Length: > 0 }
                    && attr.ConstructorArguments[0].Value is string desc
                    && !string.IsNullOrEmpty(desc))
                {
                    schema = WithDescription(schema, desc);
                }

                props.Add((propName, schema));

                // Value types are always optional in JSON-schema terms (they have a
                // sensible default). Reference types are required only when explicitly
                // non-nullable. This keeps the LLM from having to send `inline:false`
                // every time it builds a record-struct argument.
                var isOptional = memberType.IsValueType
                                 || memberType.NullableAnnotation == NullableAnnotation.Annotated;
                if (!isOptional)
                    required.Add(propName);
            }

            return new ObjectSchema(props, required);
        }

        return new PrimitiveSchema("object");
    }

    private static JsonSchema WithDescription(JsonSchema schema, string description)
    {
        // JsonSchema.Description is `init`-only, so we clone-with-description by type.
        return schema switch
        {
            PrimitiveSchema p => new PrimitiveSchema(p.TypeName, p.Format) { Description = description },
            EnumSchema e => new EnumSchema(e.Members) { Description = description },
            ArraySchema a => new ArraySchema(a.Items) { Description = description },
            ObjectSchema o => new ObjectSchema(o.Properties, o.Required) { Description = description },
            _ => schema,
        };
    }

    /// <summary>
    /// For a record / record struct, walk primary-constructor parameters first
    /// (preserving declaration order, which records expose to the LLM as their
    /// canonical shape), then any extra public instance properties.
    /// </summary>
    private static IEnumerable<IPropertySymbol> EnumeratePublicProperties(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Heuristic: the primary constructor of a record is the public constructor
        // with the largest parameter count whose parameter names all map to public
        // synthesised properties. This is the same heuristic the C# compiler uses
        // when it generates record properties.
        var primary = type.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(static c => c.Parameters.Length)
            .FirstOrDefault();

        if (primary is not null)
        {
            foreach (var p in primary.Parameters)
            {
                var prop = type.GetMembers(p.Name).OfType<IPropertySymbol>().FirstOrDefault();
                if (prop is { DeclaredAccessibility: Accessibility.Public })
                {
                    seen.Add(prop.Name);
                    yield return prop;
                }
            }
        }

        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.IsStatic) continue;
            if (prop.IsIndexer) continue;
            if (seen.Contains(prop.Name)) continue;
            if (prop.Name == "EqualityContract") continue; // compiler-emitted on records
            yield return prop;
        }
    }

    private static bool IsObjectShape(INamedTypeSymbol type)
        => type.SpecialType == SpecialType.None
           && type.TypeKind is TypeKind.Class or TypeKind.Struct;

    private static ITypeSymbol? GetCollectionElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr)
            return arr.ElementType;

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var orig = named.OriginalDefinition.ToDisplayString();
            if (orig is "System.Collections.Generic.List<T>"
                or "System.Collections.Generic.IList<T>"
                or "System.Collections.Generic.IReadOnlyList<T>"
                or "System.Collections.Generic.ICollection<T>"
                or "System.Collections.Generic.IEnumerable<T>"
                or "System.Collections.Generic.IReadOnlyCollection<T>")
            {
                return named.TypeArguments[0];
            }
        }

        return null;
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

    private static string ToSnakeCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c)
                && (char.IsLower(s[i - 1])
                    || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
