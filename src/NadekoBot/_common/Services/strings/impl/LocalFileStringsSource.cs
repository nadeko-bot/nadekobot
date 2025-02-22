#nullable disable
using Newtonsoft.Json;
using YamlDotNet.Serialization;

namespace NadekoBot.Services;

/// <summary>
///     Loads strings from the local default filepath <see cref="_responsesPath" />
/// </summary>
public class LocalFileStringsSource : IStringsSource
{
    private readonly string _responsesPath = "strings/responses";
    private readonly string _commandsPath = "strings/commands";

    public LocalFileStringsSource(
        string responsesPath = "strings/responses",
        string commandsPath = "strings/commands")
    {
        _responsesPath = responsesPath;
        _commandsPath = commandsPath;
    }

    public Dictionary<string, Dictionary<string, string>> GetResponseStrings()
    {
        var outputDict = new Dictionary<string, Dictionary<string, string>>();
        foreach (var file in Directory.GetFiles(_responsesPath))
        {
            try
            {
                var langDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file));
                var localeName = GetLocaleName(file);
                outputDict[localeName] = langDict;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading {FileName} response strings: {ErrorMessage}", file, ex.Message);
            }
        }

        return outputDict;
    }

    public Dictionary<string, Dictionary<string, CommandStrings>> GetCommandStrings()
    {
        var deserializer = new DeserializerBuilder().Build();

        var outputDict = new Dictionary<string, Dictionary<string, CommandStrings>>();
        foreach (var file in Directory.GetFiles(_commandsPath))
        {
            try
            {
                var text = File.ReadAllText(file);
                var langDict = deserializer.Deserialize<Dictionary<string, CommandStrings>>(text);
                var localeName = GetLocaleName(file);
                outputDict[localeName] = langDict;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading {FileName} command strings: {ErrorMessage}", file, ex.Message);
            }
        }

        return outputDict;
    }

    private static string GetLocaleName(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        var dotIndex = fileName.IndexOf('.') + 1;
        var secondDotIndex = fileName.LastIndexOf('.');
        return fileName.Substring(dotIndex, secondDotIndex - dotIndex);
    }
}