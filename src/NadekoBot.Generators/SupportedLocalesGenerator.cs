#nullable enable
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using YamlDotNet.Serialization;

namespace NadekoBot.Generators;

[Generator]
public class SupportedLocalesGenerator : IIncrementalGenerator
{
    private const string FILE_NAME = "supported-locales.json";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var localeFile = context.AdditionalTextsProvider
            .Where(static f => Path.GetFileName(f.Path) == FILE_NAME)
            .Collect();

        context.RegisterSourceOutput(localeFile, static (spc, files) => Execute(spc, files));
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<AdditionalText> files)
    {
        if (files.Length == 0)
            return;

        var text = files[0].GetText(context.CancellationToken)?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return;

        Dictionary<string, string> locales;
        try
        {
            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            locales = deserializer.Deserialize<Dictionary<string, string>>(text!)
                      ?? new Dictionary<string, string>();
        }
        catch
        {
            return;
        }

        if (locales.Count == 0)
            return;

        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var sw = new IndentedTextWriter(stringWriter))
        {
            sw.WriteLine("#pragma warning disable CS8981");
            sw.WriteLine("using System.Collections.Frozen;");
            sw.WriteLine();
            sw.WriteLine("namespace NadekoBot;");
            sw.WriteLine();
            sw.WriteLine("public static class SupportedLocales");
            sw.WriteLine("{");
            sw.Indent++;

            sw.WriteLine("public static readonly FrozenDictionary<string, string> All = new Dictionary<string, string>");
            sw.WriteLine("{");
            sw.Indent++;

            foreach (var kvp in locales)
            {
                var key = kvp.Key.Replace("\"", "\\\"");
                var val = kvp.Value.Replace("\"", "\\\"");
                sw.WriteLine("{{ \"{0}\", \"{1}\" }},", key, val);
            }

            sw.Indent--;
            sw.WriteLine("}.ToFrozenDictionary();");

            sw.Indent--;
            sw.WriteLine("}");

            sw.Flush();
        }

        context.AddSource("SupportedLocales.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}
