using System.Text;

namespace NadekoBot.Generators;

/// <summary>
/// Tiny JSON Schema AST. We build the schema as a tree of immutable nodes and
/// serialise once, instead of hand-stitching JSON strings -- that way escaping
/// is centralised in a single Write method and impossible to get wrong per
/// caller.
/// </summary>
internal abstract class JsonSchema
{
    public string? Description { get; init; }

    public string ToJson()
    {
        var sb = new StringBuilder(128);
        WriteTo(sb);
        return sb.ToString();
    }

    public void WriteTo(StringBuilder sb)
    {
        sb.Append('{');
        WriteBody(sb);
        if (!string.IsNullOrEmpty(Description))
        {
            sb.Append(',');
            WriteString(sb, "description");
            sb.Append(':');
            WriteString(sb, Description!);
        }
        sb.Append('}');
    }

    protected abstract void WriteBody(StringBuilder sb);

    internal static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("X4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

internal sealed class PrimitiveSchema : JsonSchema
{
    public string TypeName { get; }
    public string? Format { get; }

    public PrimitiveSchema(string typeName, string? format = null)
    {
        TypeName = typeName;
        Format = format;
    }

    protected override void WriteBody(StringBuilder sb)
    {
        WriteString(sb, "type");
        sb.Append(':');
        WriteString(sb, TypeName);
        if (Format is not null)
        {
            sb.Append(',');
            WriteString(sb, "format");
            sb.Append(':');
            WriteString(sb, Format);
        }
    }
}

internal sealed class EnumSchema : JsonSchema
{
    public IReadOnlyList<string> Members { get; }

    public EnumSchema(IReadOnlyList<string> members)
    {
        Members = members;
    }

    protected override void WriteBody(StringBuilder sb)
    {
        WriteString(sb, "type");
        sb.Append(':');
        WriteString(sb, "string");
        sb.Append(",");
        WriteString(sb, "enum");
        sb.Append(":[");
        for (var i = 0; i < Members.Count; i++)
        {
            if (i > 0) sb.Append(',');
            WriteString(sb, Members[i]);
        }
        sb.Append(']');
    }
}

internal sealed class ArraySchema : JsonSchema
{
    public JsonSchema Items { get; }

    public ArraySchema(JsonSchema items)
    {
        Items = items;
    }

    protected override void WriteBody(StringBuilder sb)
    {
        WriteString(sb, "type");
        sb.Append(':');
        WriteString(sb, "array");
        sb.Append(',');
        WriteString(sb, "items");
        sb.Append(':');
        Items.WriteTo(sb);
    }
}

internal sealed class ObjectSchema : JsonSchema
{
    public IReadOnlyList<(string Name, JsonSchema Schema)> Properties { get; }
    public IReadOnlyList<string> Required { get; }

    public ObjectSchema(
        IReadOnlyList<(string, JsonSchema)> properties,
        IReadOnlyList<string> required)
    {
        Properties = properties;
        Required = required;
    }

    protected override void WriteBody(StringBuilder sb)
    {
        WriteString(sb, "type");
        sb.Append(':');
        WriteString(sb, "object");

        if (Properties.Count > 0)
        {
            sb.Append(',');
            WriteString(sb, "properties");
            sb.Append(":{");
            for (var i = 0; i < Properties.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var (name, schema) = Properties[i];
                WriteString(sb, name);
                sb.Append(':');
                schema.WriteTo(sb);
            }
            sb.Append('}');
        }

        if (Required.Count > 0)
        {
            sb.Append(',');
            WriteString(sb, "required");
            sb.Append(":[");
            for (var i = 0; i < Required.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, Required[i]);
            }
            sb.Append(']');
        }
    }
}
