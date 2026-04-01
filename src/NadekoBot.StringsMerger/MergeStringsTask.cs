#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Core;

public class MergeStringsTask : Task
{
    [Required]
    public ITaskItem[] InputYmls { get; set; } = [];


    // where to write the merged file
    [Required]
    public string OutputDir { get; set; } = string.Empty;

    private readonly Regex _resRegex = new(@"^res(?:\.(?<lang>.+))?\.yml$", RegexOptions.IgnoreCase);
    private readonly Regex _cmdRegex = new(@"^cmds(?:\.(?<lang>.+))?\.yml$", RegexOptions.IgnoreCase);
    private const string I18N_FILENAME = "res.i18n.yml";

    IDeserializer deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    ISerializer serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithQuotingNecessaryStrings(quoteYaml1_1Strings: true)
        .Build();

    public override bool Execute()
    {
        return ExecuteResponses() && ExecuteCommands() && ExecuteNames();
    }

    public sealed class CmdPoco
    {
        [YamlMember(Alias = "desc")]
        public string Desc { get; set; }

        [YamlMember(Alias = "ex")]
        public string[] Ex { get; set; }

        [YamlMember(Alias = "params")]
        public Dictionary<string, OptPoco>[] Params { get; set; }

        public class OptPoco
        {
            [YamlMember(Alias = "desc")]
            public string Desc { get; set; }
        }
    }

    public bool ExecuteCommands()
    {
        try
        {
            // lang → merged dictionary
            var mergedByLang = new Dictionary<string, Dictionary<string, CmdPoco>>(StringComparer.OrdinalIgnoreCase);
            var processedFileCount = 0;

            foreach (var item in InputYmls)
            {
                var filePath = item.ItemSpec;
                var fileName = Path.GetFileName(filePath);
                
                var match = _cmdRegex.Match(fileName);

                if (!match.Success)
                {
                    continue;
                }

                var lang = match.Groups["lang"].Value;

                try
                {
                    var text = File.ReadAllText(filePath);
                    var incoming = deserializer.Deserialize<Dictionary<string, CmdPoco>>(text);

                    if (!mergedByLang.TryGetValue(lang, out var existing))
                    {
                        // First file for this lang
                        mergedByLang[lang] = new(incoming, StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        // Merge: union of keys, incoming wins on conflicts
                        foreach (var kv in incoming)
                            existing[kv.Key] = kv.Value;
                    }

                    processedFileCount++;
                }
                catch (YamlException yex)
                {
                    Log.LogError($"YAML parsing error in '{filePath}': {yex.Message}");
                    Log.LogError(yex.ToString());
                }
                catch (Exception ex)
                {
                    Log.LogError($"Error processing '{filePath}': {ex.Message}");
                }
            }

            if (processedFileCount == 0)
            {
                Log.LogError("No valid cmds YAML files were processed.");
                return false;
            }

            // Write merged YAML out
            var outResDir = Path.Combine(OutputDir, "commands");
            Directory.CreateDirectory(outResDir);

            foreach (var kvp in mergedByLang)
            {
                var lang = kvp.Key;
                var data = kvp.Value;
                var outputPath = Path.Combine(outResDir,
                    string.IsNullOrWhiteSpace(lang) ? "cmds.en-US.yml" : $"cmds.{lang}.yml");

                try
                {
                    var yaml = serializer.Serialize(data);
                    File.WriteAllText(outputPath, yaml);
                    Log.LogMessage(MessageImportance.Normal,
                        $"Merged {data.Count} entries for '{lang}' → {outputPath}");
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to write '{outputPath}': {ex.Message}");
                    return false;
                }
            }

            Log.LogMessage(MessageImportance.Low,
                $"Successfully processed {processedFileCount} files into {mergedByLang.Count} language YAMLs.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"Task failed: {ex.Message}");
            return false;
        }
    }

    public bool ExecuteNames()
    {
        var merged = new Dictionary<string, string[]>();

        foreach (var item in InputYmls)
        {
            var filePath = item.ItemSpec;

            if (!filePath.EndsWith("names.yml"))
                continue;
            
            try
            {
                var text = File.ReadAllText(filePath);
                var data = deserializer.Deserialize<Dictionary<string, string[]>>(text);

                foreach (var kvp in data)
                {
                    if (merged.ContainsKey(kvp.Key))
                    {
                        Log.LogWarning($"Duplicate alias found: {kvp.Key}");
                    }

                    merged.Add(kvp.Key, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error processing {filePath}: ", ex.Message);
            }
        }

        var output = serializer.Serialize(merged);
        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(Path.Combine(OutputDir, "names.yml"), output);
        return true;
    }

    public bool ExecuteResponses()
    {
        try
        {
            var mergedByLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var processedFileCount = 0;

            foreach (var item in InputYmls)
            {
                var filePath = item.ItemSpec;
                var fileName = Path.GetFileName(filePath);

                if (string.Equals(fileName, I18N_FILENAME, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var text = File.ReadAllText(filePath);
                        var i18nData = deserializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(text);
                        if (i18nData == null) continue;

                        foreach (var langEntry in i18nData)
                        {
                            var lang = langEntry.Key;
                            var incoming = langEntry.Value;
                            if (incoming == null) continue;

                            if (!mergedByLang.TryGetValue(lang, out var existing))
                            {
                                mergedByLang[lang] = new(incoming, StringComparer.OrdinalIgnoreCase);
                            }
                            else
                            {
                                foreach (var kv in incoming)
                                    existing[kv.Key] = kv.Value;
                            }
                        }

                        processedFileCount++;
                    }
                    catch (YamlException yex)
                    {
                        Log.LogError($"YAML parsing error in '{filePath}': {yex.Message}");
                        Log.LogError(yex.ToString());
                    }
                    catch (Exception ex)
                    {
                        Log.LogError($"Error processing '{filePath}': {ex.Message}");
                    }

                    continue;
                }

                var match = _resRegex.Match(fileName);
                if (!match.Success)
                {
                    Log.LogMessage(MessageImportance.Low,
                        $"Skipping file '{fileName}' as it doesn't match res.<lang>.yml pattern.");
                    continue;
                }

                var matchedLang = match.Groups["lang"].Value;

                try
                {
                    var text2 = File.ReadAllText(filePath);
                    var incoming2 = deserializer.Deserialize<Dictionary<string, string>>(text2);

                    if (!mergedByLang.TryGetValue(matchedLang, out var existing2))
                    {
                        mergedByLang[matchedLang] = new(incoming2, StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        foreach (var kv in incoming2)
                            existing2[kv.Key] = kv.Value;
                    }

                    processedFileCount++;
                }
                catch (YamlException yex)
                {
                    Log.LogError($"YAML parsing error in '{filePath}': {yex.Message}");
                    Log.LogError(yex.ToString());
                }
                catch (Exception ex)
                {
                    Log.LogError($"Error processing '{filePath}': {ex.Message}");
                }
            }

            if (processedFileCount == 0)
            {
                Log.LogError("No valid YAML files were processed.");
                return false;
            }

            var outResDir = Path.Combine(OutputDir, "responses");
            Directory.CreateDirectory(outResDir);

            foreach (var kvp in mergedByLang)
            {
                var lang = kvp.Key;
                var data = kvp.Value;
                var outputPath = Path.Combine(outResDir,
                    string.IsNullOrWhiteSpace(lang) ? "responses.en-US.yml" : $"responses.{lang}.yml");

                try
                {
                    var yaml = serializer.Serialize(data);
                    File.WriteAllText(outputPath, yaml);
                    Log.LogMessage(MessageImportance.Normal,
                        $"Merged {data.Count} entries for '{lang}' → {outputPath}");
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to write '{outputPath}': {ex.Message}");
                    return false;
                }
            }

            Log.LogMessage(MessageImportance.Low,
                $"Successfully processed {processedFileCount} files into {mergedByLang.Count} language YAMLs.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"Task failed: {ex.Message}");
            return false;
        }
    }
}