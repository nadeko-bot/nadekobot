using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace NadekoBot.Services;

// Rewrites the text of a config file from an old version before it is read, for keys which a later version renamed or reshaped.
public static class LegacyYaml
{
    private static readonly YamlScalarNode _versionKey = new("version");

    public static string EditBelowVersion(string yaml, int version, Action<YamlMappingNode, int> edit)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException)
        {
            // the normal load reports the error
            return yaml;
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return yaml;

        // Without a readable version the file cannot be proven old, and an edit can remove settings.
        if (!TryGetScalar(root, _versionKey, out var text)
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileVersion)
            || fileVersion >= version)
            return yaml;

        edit(root, fileVersion);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    public static void Remove(YamlMappingNode node, string key)
        => node.Children.Remove(new YamlScalarNode(key));

    // Keeps the new key when the file already has it.
    public static void Rename(YamlMappingNode node, string from, string to, Func<string, string>? convert = null)
    {
        var fromKey = new YamlScalarNode(from);
        if (!TryGetScalar(node, fromKey, out var value))
            return;

        node.Children.Remove(fromKey);

        var toKey = new YamlScalarNode(to);
        if (node.Children.ContainsKey(toKey))
            return;

        node.Children[toKey] = new YamlScalarNode(convert is null ? value : convert(value));
    }

    public static void Convert(YamlMappingNode node, string key, Func<string, string> convert)
    {
        var scalarKey = new YamlScalarNode(key);
        if (TryGetScalar(node, scalarKey, out var value))
            node.Children[scalarKey] = new YamlScalarNode(convert(value));
    }

    private static bool TryGetScalar(YamlMappingNode node, YamlScalarNode key, out string value)
    {
        if (node.Children.TryGetValue(key, out var child) && child is YamlScalarNode { Value: { } text })
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
