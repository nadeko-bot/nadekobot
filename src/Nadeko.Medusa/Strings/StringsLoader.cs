using System.Diagnostics.CodeAnalysis;
using Serilog;
using YamlDotNet.Serialization;

namespace NadekoBot.Medusa;

/// <summary>
///     Loads strings from the shortcut or localizable path
/// </summary>
public class StringsLoader
{
    private readonly string _localizableResponsesPath;
    private readonly string _shortcutResponsesFile;
    
    private readonly string _localizableCommandsPath;
    private readonly string _shortcutCommandsFile;

    public StringsLoader(string basePath)
    {
        _localizableResponsesPath = Path.Join(basePath, "strings/res");
        _shortcutResponsesFile = Path.Join(basePath, "res.yml");
        
        _localizableCommandsPath = Path.Join(basePath, "strings/cmds");
        _shortcutCommandsFile = Path.Join(basePath, "cmds.yml");
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, CommandStrings>> GetCommandStrings()
    {
        var outputDict = new Dictionary<string, IReadOnlyDictionary<string, CommandStrings>>();

        if (File.Exists(_shortcutCommandsFile))
        {
            if (TryLoadCommandsFromFile(_shortcutCommandsFile, out var dict, out _))
            {
                outputDict["en-us"] = dict;
            }

            return outputDict;
        }

        if (Directory.Exists(_localizableCommandsPath))
        {
            foreach (var cmdsFile in Directory.EnumerateFiles(_localizableCommandsPath))
            {
                if (TryLoadCommandsFromFile(cmdsFile, out var dict, out var locale) && locale is not null)
                {
                    outputDict[locale.ToLowerInvariant()] = dict;
                }
            }
        }

        return outputDict;
    }

    
    private static readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
    private static bool TryLoadCommandsFromFile(string file,
        [NotNullWhen(true)] out IReadOnlyDictionary<string, CommandStrings>? strings,
        out string? localeName)
    {
        try
        {
            var text = File.ReadAllText(file);
            strings = _deserializer.Deserialize<Dictionary<string, CommandStrings>?>(text)
                      ?? new();
            localeName = GetLocaleName(file);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading {FileName} command strings: {ErrorMessage}", file, ex.Message);
        }

        strings = null;
        localeName = null;
        return false;
    }


    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetResponseStrings()
    {
        var outputDict = new Dictionary<string, IReadOnlyDictionary<string, string>>();

        // try to load a shortcut file
        if (File.Exists(_shortcutResponsesFile))
        {
            if (TryLoadResponsesFromFile(_shortcutResponsesFile, out var dict, out _))
            {
                outputDict["en-us"] = dict;
            }

            return outputDict;
        }

        if (!Directory.Exists(_localizableResponsesPath))
            return outputDict;
        
        // if shortcut file doesn't exist, try to load localizable files
        foreach (var file in Directory.GetFiles(_localizableResponsesPath))
        {
            if (TryLoadResponsesFromFile(file, out var strings, out var localeName) && localeName is not null)
            {
                outputDict[localeName.ToLowerInvariant()] = strings;
            }
        }

        return outputDict;
    }

    private static bool TryLoadResponsesFromFile(string file,
        [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? strings,
        out string? localeName)
    {
        try
        {
            strings = _deserializer.Deserialize<Dictionary<string, string>?>(File.ReadAllText(file));
            if (strings is null)
            {
                localeName = null;
                return false;
            }

            localeName = GetLocaleName(file).ToLowerInvariant();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading {FileName} response strings: {ErrorMessage}", file, ex.Message);
            strings = null;
            localeName = null;
            return false;
        }
    }

    private static string GetLocaleName(string fileName)
        => Path.GetFileNameWithoutExtension(fileName);
}