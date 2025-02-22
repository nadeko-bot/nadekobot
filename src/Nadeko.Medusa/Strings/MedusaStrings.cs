using System.Globalization;
using Serilog;

namespace NadekoBot.Medusa;

public class MedusaStrings : IMedusaStrings
{
    /// <summary>
    ///     Used as failsafe in case response key doesn't exist in the selected or default language.
    /// </summary>
    private readonly CultureInfo _usCultureInfo = new("en-US");
    
    private readonly IMedusaStringsProvider _stringsProvider;

    public MedusaStrings(IMedusaStringsProvider stringsProvider)
    {
        _stringsProvider = stringsProvider;
    }

    private string? GetString(string key, CultureInfo cultureInfo)
        => _stringsProvider.GetText(cultureInfo.Name, key);

    public string? GetText(string key, CultureInfo cultureInfo)
        => GetString(key, cultureInfo)
           ?? GetString(key, _usCultureInfo);

    public string? GetText(string key, CultureInfo cultureInfo, params object[] data)
    {
        var text = GetText(key, cultureInfo);

        if (string.IsNullOrWhiteSpace(text))
            return null;
        
        try
        {
            return string.Format(text, data);
        }
        catch (FormatException)
        {
            Log.Warning(" Key '{Key}' is not properly formatted in '{LanguageName}' response strings",
                key,
                cultureInfo.Name);
            
            return $"⚠️ Response string key '{key}' is not properly formatted. Please report this.\n\n{text}";
        }
    }
    
    public CommandStrings GetCommandStrings(string commandName, CultureInfo cultureInfo)
    {
        var cmdStrings = _stringsProvider.GetCommandStrings(cultureInfo.Name, commandName);
        if (cmdStrings is null)
        {
            if (cultureInfo.Name == _usCultureInfo.Name)
            {
                Log.Warning("'{CommandName}' doesn't exist in 'en-US' command strings for one of the medusae",
                    commandName);

                return new(null, null);
            }

            Log.Information("Missing '{CommandName}' command strings for the '{LocaleName}' locale",
                commandName,
                cultureInfo.Name);
            
            return GetCommandStrings(commandName, _usCultureInfo);
        }

        return cmdStrings.Value;
    }

    public string? GetDescription(CultureInfo? locale = null)
        => GetText("medusa.description", locale ?? _usCultureInfo);

    public static MedusaStrings CreateDefault(string basePath)
        => new MedusaStrings(new LocalMedusaStringsProvider(new(basePath)));
    
    public void Reload()
        => _stringsProvider.Reload();
}