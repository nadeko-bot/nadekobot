using System.CodeDom.Compiler;

namespace NadekoBot.Generators;

/// <summary>
/// Emits the per-parameter deserialization code (the section between
/// permission checks and the adapter call). Split per ParamKind so each
/// branch is short and self-contained.
///
/// Every error path goes through <see cref="AiToolGenerator.EmitErrorReturn"/>
/// to keep the error shape consistent across all parameters.
/// </summary>
internal static class ParamEmitter
{
    public static void Emit(IndentedTextWriter w, ToolParamInfo p)
    {
        switch (p.Kind)
        {
            case ParamKind.Ulong: EmitUlong(w, p); return;
            case ParamKind.Int: EmitInt(w, p); return;
            case ParamKind.Long: EmitLong(w, p); return;
            case ParamKind.Bool: EmitBool(w, p); return;
            case ParamKind.String: EmitString(w, p); return;
            case ParamKind.Enum: EmitEnum(w, p); return;
            case ParamKind.Double: EmitDouble(w, p); return;
            case ParamKind.JsonElement: EmitJsonElement(w, p); return;
            case ParamKind.Object: EmitObject(w, p); return;
        }
    }

    private static void EmitUlong(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        var pv = ParsedVar(p);

        if (p.IsOptional)
        {
            w.WriteLine($"ulong? {v} = null;");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && IsPresent({el}))");
            w.WriteLine("{");
            w.Indent++;
            w.WriteLine($"var _raw_{p.Name} = {el}.ValueKind == JsonValueKind.String ? {el}.GetString() : {el}.GetRawText();");
            w.WriteLine($"if (ulong.TryParse(_raw_{p.Name}, out var {pv})) {v} = {pv};");
            w.Indent--;
            w.WriteLine("}");
        }
        else
        {
            w.WriteLine($"ulong {v};");
            w.WriteLine("{");
            w.Indent++;
            w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var {el}) || !IsPresent({el}))");
            w.Indent++;
            EmitMissingError(w, p);
            w.Indent--;
            w.WriteLine($"var _raw_{p.Name} = {el}.ValueKind == JsonValueKind.String ? {el}.GetString() : {el}.GetRawText();");
            w.WriteLine($"if (!ulong.TryParse(_raw_{p.Name}, out {v}))");
            w.Indent++;
            EmitInvalidError(w, p, "must be a valid ID.");
            w.Indent--;
            w.Indent--;
            w.WriteLine("}");
        }
    }

    private static void EmitInt(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        var pv = ParsedVar(p);

        if (p.IsOptional)
        {
            var def = p.HasDefault && p.DefaultValue is not null ? p.DefaultValue.ToString() : "0";
            w.WriteLine($"var {v} = {def};");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && {el}.TryGetInt32(out var {pv}))");
            w.Indent++;
            w.WriteLine($"{v} = {pv};");
            w.Indent--;
        }
        else
        {
            w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var {el}) || !{el}.TryGetInt32(out var {v}))");
            w.WriteLine("{");
            w.Indent++;
            EmitInvalidError(w, p, "must be an integer.");
            w.Indent--;
            w.WriteLine("}");
        }
    }

    private static void EmitLong(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        var pv = ParsedVar(p);

        if (p.IsOptional)
        {
            var def = p.HasDefault && p.DefaultValue is not null ? p.DefaultValue.ToString() : "0";
            w.WriteLine($"var {v} = {def}L;");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && {el}.TryGetInt64(out var {pv}))");
            w.Indent++;
            w.WriteLine($"{v} = {pv};");
            w.Indent--;
        }
        else
        {
            w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var {el}) || !{el}.TryGetInt64(out var {v}))");
            w.WriteLine("{");
            w.Indent++;
            EmitInvalidError(w, p, "must be a number.");
            w.Indent--;
            w.WriteLine("}");
        }
    }

    private static void EmitBool(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        var def = p.HasDefault && p.DefaultValue is bool bv && bv ? "true" : "false";
        w.WriteLine($"var {v} = {def};");
        w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && {el}.ValueKind is JsonValueKind.True or JsonValueKind.False)");
        w.Indent++;
        w.WriteLine($"{v} = {el}.GetBoolean();");
        w.Indent--;
    }

    private static void EmitString(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);

        if (p.IsOptional)
        {
            // If a string default was provided, the parameter is non-nullable in the
            // adapter signature -- emit the local as `string` so we don't pass `string?`
            // into a non-nullable slot. Otherwise default to `null` and emit `string?`.
            string declType, def;
            if (p.HasDefault && p.DefaultValue is string sv)
            {
                declType = "string";
                def = AiToolGenerator.Quote(sv);
            }
            else
            {
                declType = "string?";
                def = "null";
            }
            w.WriteLine($"{declType} {v} = {def};");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && {el}.ValueKind == JsonValueKind.String)");
            w.Indent++;
            w.WriteLine($"{v} = {el}.GetString()!;");
            w.Indent--;
        }
        else
        {
            w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var {el}) || {el}.ValueKind != JsonValueKind.String)");
            w.WriteLine("{");
            w.Indent++;
            EmitMissingError(w, p);
            w.Indent--;
            w.WriteLine("}");
            w.WriteLine($"var {v} = {el}.GetString()!;");
        }
    }

    private static void EmitEnum(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        var pv = ParsedVar(p);
        var membersList = string.Join(", ", p.EnumMembers);

        if (p.IsOptional)
        {
            w.WriteLine($"{p.TypeFull}? {v} = null;");
            w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && {el}.ValueKind == JsonValueKind.String");
            w.WriteLine($"    && System.Enum.TryParse<{p.TypeFull}>({el}.GetString(), true, out var {pv}))");
            w.Indent++;
            w.WriteLine($"{v} = {pv};");
            w.Indent--;
        }
        else
        {
            w.WriteLine($"if (!_args.TryGetProperty(\"{p.Name}\", out var {el}) || {el}.ValueKind != JsonValueKind.String");
            w.WriteLine($"    || !System.Enum.TryParse<{p.TypeFull}>({el}.GetString(), true, out var {v}))");
            w.WriteLine("{");
            w.Indent++;
            EmitInvalidError(w, p, $"must be one of: {membersList}.");
            w.Indent--;
            w.WriteLine("}");
        }
    }

    private static void EmitDouble(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        var pv = ParsedVar(p);
        var def = p.HasDefault && p.DefaultValue is not null ? p.DefaultValue + "d" : "0d";
        w.WriteLine($"var {v} = {def};");
        w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && {el}.TryGetDouble(out var {pv}))");
        w.Indent++;
        w.WriteLine($"{v} = {pv};");
        w.Indent--;
    }

    private static void EmitJsonElement(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);
        w.WriteLine($"var {v} = _args.TryGetProperty(\"{p.Name}\", out var {el})");
        w.WriteLine($"    ? {el}");
        w.WriteLine($"    : default(global::System.Text.Json.JsonElement);");
    }

    private static void EmitObject(IndentedTextWriter w, ToolParamInfo p)
    {
        var v = Var(p);
        var el = ElVar(p);

        w.WriteLine($"{p.TypeFull} {v} = default({p.TypeFull})!;");
        w.WriteLine($"if (_args.TryGetProperty(\"{p.Name}\", out var {el}) && IsPresent({el}))");
        w.WriteLine("{");
        w.Indent++;
        w.WriteLine("try");
        w.WriteLine("{");
        w.Indent++;
        w.WriteLine($"{v} = System.Text.Json.JsonSerializer.Deserialize<{p.TypeFull}>({el}.GetRawText(), global::NadekoBot.Modules.Utility.AiAgent.AiToolJsonOptions.Options)!;");
        w.Indent--;
        w.WriteLine("}");
        w.WriteLine("catch (System.Text.Json.JsonException _jx)");
        w.WriteLine("{");
        w.Indent++;
        var msgExpr = $"\"{p.Name}: \" + _jx.Message";
        AiToolGenerator.EmitErrorReturn(w, AiToolGenerator.Quote("invalid_argument"), msgExpr);
        w.Indent--;
        w.WriteLine("}");
        w.Indent--;
        w.WriteLine("}");

        if (!p.IsOptional)
        {
            w.WriteLine($"if ({v} is null)");
            w.WriteLine("{");
            w.Indent++;
            EmitMissingError(w, p);
            w.Indent--;
            w.WriteLine("}");
        }
    }

    private static string Var(ToolParamInfo p) => $"_p_{p.Name}";
    private static string ElVar(ToolParamInfo p) => $"_el_{p.Name}";
    private static string ParsedVar(ToolParamInfo p) => $"_parsed_{p.Name}";

    private static void EmitMissingError(IndentedTextWriter w, ToolParamInfo p)
        => AiToolGenerator.EmitErrorReturn(w,
            AiToolGenerator.Quote("invalid_argument"),
            AiToolGenerator.Quote($"{p.Name} is required."));

    private static void EmitInvalidError(IndentedTextWriter w, ToolParamInfo p, string suffix)
        => AiToolGenerator.EmitErrorReturn(w,
            AiToolGenerator.Quote("invalid_argument"),
            AiToolGenerator.Quote($"{p.Name} {suffix}"));
}
