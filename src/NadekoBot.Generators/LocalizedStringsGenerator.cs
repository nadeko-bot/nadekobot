#nullable enable
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace NadekoBot.Generators;

[Generator]
public class LocalizedStringsGenerator : IIncrementalGenerator
{
    private static readonly Regex _placeholderRegex = new(@"\{(?<num>\d+)[}:]", RegexOptions.Compiled);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var resFiles = context.AdditionalTextsProvider
            .Where(static f => Path.GetFileName(f.Path) == "res.yml");

        var collected = resFiles.Collect();

        context.RegisterSourceOutput(collected, static (spc, files) => Execute(spc, files));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<AdditionalText> files)
    {
        var mergedDict = new Dictionary<string, string>();

        foreach (var file in files)
        {
            var text = file.GetText(context.CancellationToken)?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var fields = ParseYaml(text!);
            foreach (var field in fields)
            {
                mergedDict[field.Key] = field.Value;
            }
        }

        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var sw = new IndentedTextWriter(stringWriter))
        {
            sw.WriteLine("#pragma warning disable CS8981");
            sw.WriteLine("namespace NadekoBot;");
            sw.WriteLine();

            sw.WriteLine("public static class strs");
            sw.WriteLine("{");
            sw.Indent++;

            var typedParamStrings = new List<string>(10);
            foreach (var field in mergedDict)
            {
                var matches = _placeholderRegex.Matches(field.Value);
                var max = 0;
                foreach (Match match in matches)
                {
                    max = Math.Max(max, int.Parse(match.Groups["num"].Value) + 1);
                }

                typedParamStrings.Clear();
                var typeParams = new string[max];
                var passedParamString = string.Empty;
                for (var i = 0; i < max; i++)
                {
                    typedParamStrings.Add($"in T{i} p{i}");
                    passedParamString += $", p{i}";
                    typeParams[i] = $"T{i}";
                }

                var sig = string.Empty;
                var typeParamStr = string.Empty;
                if (max > 0)
                {
                    sig = $"({string.Join(", ", typedParamStrings)})";
                    typeParamStr = $"<{string.Join(", ", typeParams)}>";
                }

                sw.WriteLine("public static LocStr {0}{1}{2} => new LocStr(\"{3}\"{4});",
                    field.Key,
                    typeParamStr,
                    sig,
                    field.Key,
                    passedParamString);
            }

            sw.Indent--;
            sw.WriteLine("}");

            sw.Flush();
        }

        context.AddSource("strs.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static Dictionary<string, string> ParseYaml(string dataText)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            return deserializer.Deserialize<Dictionary<string, string>>(dataText)
                   ?? new Dictionary<string, string>();
        }
        catch (YamlException)
        {
            return new Dictionary<string, string>();
        }
        catch (Exception)
        {
            return new Dictionary<string, string>();
        }
    }
}
