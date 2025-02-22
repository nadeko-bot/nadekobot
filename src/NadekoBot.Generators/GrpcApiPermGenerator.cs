#nullable enable
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Newtonsoft.Json;

namespace NadekoBot.Generators
{
    public readonly record struct MethodPermData
    {
        public readonly ImmutableArray<(string Name, string Value)> MethodPerms;
        public readonly ImmutableArray<string> NoAuthRequired;

        public MethodPermData(ImmutableArray<(string Name, string Value)> methodPerms,
            ImmutableArray<string> noAuthRequired)
        {
            MethodPerms = methodPerms;
            NoAuthRequired = noAuthRequired;
        }
    }


    [Generator]
    public class GrpcApiPermGenerator : IIncrementalGenerator
    {
        public const string GRPC_API_PERM_ATTRIBUTE =
            """
            namespace NadekoBot.GrpcApi;

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public class GrpcApiPermAttribute : System.Attribute
            {
                public GuildPerm Value { get; }
                public GrpcApiPermAttribute(GuildPerm value) => Value = value;
            }
            """;
        
        public const string GRPC_NO_AUTH_REQUIRED_ATTRIBUTE =
            """
            namespace NadekoBot.GrpcApi;

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public class GrpcNoAuthRequiredAttribute : System.Attribute
            {
            }
            """;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource("GrpcApiPermAttribute.cs",
                SourceText.From(GRPC_API_PERM_ATTRIBUTE, Encoding.UTF8)));
            
            context.RegisterPostInitializationOutput(ctx => ctx.AddSource("GrpcNoAuthRequiredAttribute.cs",
                SourceText.From(GRPC_NO_AUTH_REQUIRED_ATTRIBUTE, Encoding.UTF8)));

            var perms = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "NadekoBot.GrpcApi.GrpcApiPermAttribute",
                    predicate: static (s, _) => s is MethodDeclarationSyntax,
                    transform: static (ctx, _) => GetMethodSemanticTargets(ctx.SemanticModel, ctx.TargetNode))
                .Where(static m => m is not null)
                .Select(static (x, _) => x!.Value)
                .Collect();


            var all = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "NadekoBot.GrpcApi.GrpcNoAuthRequiredAttribute",
                    predicate: static (s, _) => s is MethodDeclarationSyntax,
                    transform: static (ctx, _) => GetNoAuthMethodName(ctx.SemanticModel, ctx.TargetNode))
                .Collect()
                .Combine(perms)
                .Select((x, _) => new MethodPermData(x.Right, x.Left));
                
            context.RegisterSourceOutput(all,
                static (spc, source) => Execute(source, spc));
        }

        private static string GetNoAuthMethodName(SemanticModel model, SyntaxNode node)
            => ((MethodDeclarationSyntax)node).Identifier.Text;

        private static (string Name, string Value)? GetMethodSemanticTargets(SemanticModel model, SyntaxNode node)
        {
            var method = (MethodDeclarationSyntax)node;

            var name = method.Identifier.Text;
            var attr = method.AttributeLists
                .SelectMany(x => x.Attributes)
                .FirstOrDefault();

            if (attr is null)
                return null;

            return (name, attr.ArgumentList?.Arguments[0].ToString() ?? "__missing_perm__");
        }

        private static void Execute(MethodPermData data, SourceProductionContext ctx)
        {
            using (var stringWriter = new StringWriter())
            using (var sw = new IndentedTextWriter(stringWriter))
            {
                sw.WriteLine("using System.Collections.Frozen;");
                sw.WriteLine();
                sw.WriteLine("namespace NadekoBot.GrpcApi;");
                sw.WriteLine();

                sw.WriteLine("public partial class GrpcApiPermsInterceptor");
                sw.WriteLine("{");

                sw.Indent++;

                sw.WriteLine(
                    "private static FrozenDictionary<string, GuildPerm> _perms = new Dictionary<string, GuildPerm>()");
                sw.WriteLine("{");

                sw.Indent++;
                foreach (var field in data.MethodPerms)
                {
                    sw.WriteLine("{{ \"{0}\", {1} }},", field.Name, field.Value);
                }

                sw.Indent--;
                sw.WriteLine("}.ToFrozenDictionary();");

                sw.WriteLine();
                sw.WriteLine("private static FrozenSet<string> _noAuthRequired = new HashSet<string>()");
                sw.WriteLine("{");

                sw.Indent++;
                foreach (var noauth in data.NoAuthRequired)
                {
                    sw.WriteLine("{{ \"{0}\" }},", noauth);
                }

                sw.WriteLine("");

                sw.Indent--;
                sw.WriteLine("}.ToFrozenSet();");

                sw.Indent--;
                sw.WriteLine("}");

                sw.Flush();
                ctx.AddSource("GrpcApiInterceptor.g.cs", stringWriter.ToString());
            }
        }

        private List<TranslationPair> GetFields(string? dataText)
        {
            if (string.IsNullOrWhiteSpace(dataText))
                return new();

            Dictionary<string, string> data;
            try
            {
                var output = JsonConvert.DeserializeObject<Dictionary<string, string>>(dataText!);
                if (output is null)
                    return new();

                data = output;
            }
            catch
            {
                Debug.WriteLine("Failed parsing responses file.");
                return new();
            }

            var list = new List<TranslationPair>();
            foreach (var entry in data)
            {
                list.Add(new(
                    entry.Key,
                    entry.Value
                ));
            }

            return list;
        }
    }
}