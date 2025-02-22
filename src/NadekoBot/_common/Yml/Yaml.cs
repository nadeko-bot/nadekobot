#nullable disable
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NadekoBot.Common.Yml;

public class Yaml
{
    public static ISerializer Serializer
        => new SerializerBuilder()
           .WithTypeInspector(inner => new CommentGatheringTypeInspector(inner))
                                  .DisableAliases()
                                  .WithEmissionPhaseObjectGraphVisitor(args
                                      => new CommentsObjectGraphVisitor(args.InnerVisitor))
                                  .WithEventEmitter(args => new MultilineScalarFlowStyleEmitter(args))
                                  .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                  .WithIndentedSequences()
                                  .WithTypeConverter(new Rgba32Converter())
                                  .WithTypeConverter(new CultureInfoConverter())
                                  .WithTypeConverter(new UriConverter())
                                  .Build();

    public static IDeserializer Deserializer
        => new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                                    .WithTypeConverter(new Rgba32Converter())
                                    .WithTypeConverter(new CultureInfoConverter())
                                    .WithTypeConverter(new UriConverter())
                                    .IgnoreUnmatchedProperties()
                                    .Build();
}