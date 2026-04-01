using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Discord.Commands;
using NadekoBot.Common;
using NadekoBot.Common.Attributes;
using NadekoBot.Services;


namespace NadekoBot.Tests
{
    public class CommandStringsTests
    {
        private const string responsesPath = "strings/responses";
        private const string commandsPath = "strings/commands";
        private const string aliasesPath = "strings/names.yml";

        [Test]
        public void AllCommandNamesHaveStrings()
        {
            var stringsSource = new LocalFileStringsSource(
                responsesPath,
                commandsPath);
            var strings = new MemoryBotStringsProvider(stringsSource);

            var culture = new CultureInfo("en-US");

            var isSuccess = true;
            foreach (var (methodName, _) in CommandNameLoadHelper.LoadAliases(aliasesPath))
            {
                var cmdStrings = strings.GetCommandStrings(culture.Name, methodName);
                if (cmdStrings is null)
                {
                    isSuccess = false;
                    TestContext.Out.WriteLine($"{methodName} doesn't exist in cmds.en-US.yml");
                }
            }

            Assert.That(isSuccess, Is.True);
        }

        private static string[] GetCommandMethodNames()
            => typeof(Bot).Assembly
                .GetExportedTypes()
                .Where(type => type.IsClass && !type.IsAbstract)
                .Where(type => typeof(NadekoModule).IsAssignableFrom(type) // if its a top level module
                               || !(type.GetCustomAttribute<GroupAttribute>(true) is null)) // or a submodule
                .SelectMany(x => x.GetMethods()
                    .Where(mi => mi.CustomAttributes
                        .Any(ca => ca.AttributeType == typeof(CmdAttribute))))
                .Select(x => x.Name.ToLowerInvariant())
                .ToArray();

        [Test]
        public void AllCommandMethodsHaveNames()
        {
            var allAliases = CommandNameLoadHelper.LoadAliases(
                aliasesPath);

            var methodNames = GetCommandMethodNames();

            var isSuccess = true;
            foreach (var methodName in methodNames)
            {
                if (!allAliases.TryGetValue(methodName, out _))
                {
                    TestContext.Error.WriteLine($"{methodName} is missing an alias.");
                    isSuccess = false;
                }
            }

            Assert.That(isSuccess, Is.True);
        }

        [Test]
        public void NoObsoleteAliases()
        {
            var allAliases = CommandNameLoadHelper.LoadAliases(aliasesPath);

            var methodNames = GetCommandMethodNames()
                .ToHashSet();

            var isSuccess = true;

            foreach (var item in allAliases)
            {
                var methodName = item.Key;

                if (!methodNames.Contains(methodName))
                {
                    TestContext.WriteLine($"'{methodName}' from strings/names.yml doesn't have a matching command method.");
                    isSuccess = false;
                }
            }

            if (isSuccess)
                Assert.Pass();
            else
                Assert.Warn("There are some unused entries in strings/names.yml");
        }

        [Test]
        public void NoObsoleteCommandStrings()
        {
            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);

            var culture = new CultureInfo("en-US");

            var methodNames = GetCommandMethodNames()
                .ToHashSet();

            var isSuccess = true;
            // var allCommandNames = CommandNameLoadHelper.LoadCommandStrings(commandsPath));
            foreach (var entry in stringsSource.GetCommandStrings()[culture.Name])
            {
                var cmdName = entry.Key;

                if (!methodNames.Contains(cmdName))
                {
                    TestContext.Out.WriteLine(
                        $"'{cmdName}' from commands.en-US.yml doesn't have a matching command method.");
                    isSuccess = false;
                }
            }

            Assert.That(isSuccess, Is.True, "There are some unused command strings in strings/commands.en-US.yml");
        }

        [Test]
        public void NoOrphanedResponseStrings()
        {
            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);
            var allKeys = stringsSource.GetResponseStrings()["en-US"].Keys.ToHashSet();

            var usedKeys = new HashSet<string>();
            var srcPath = Path.GetFullPath(Path.Combine("../../../..", "NadekoBot"));
            var csFiles = Directory.GetFiles(srcPath, "*.cs", SearchOption.AllDirectories);
            var strsPattern = new System.Text.RegularExpressions.Regex(@"strs\.(\w+)");

            foreach (var file in csFiles)
            {
                var content = File.ReadAllText(file);
                foreach (System.Text.RegularExpressions.Match match in strsPattern.Matches(content))
                    usedKeys.Add(match.Groups[1].Value);
            }

            var orphaned = allKeys.Except(usedKeys).OrderBy(x => x).ToList();
            if (orphaned.Count > 0)
            {
                foreach (var key in orphaned)
                    TestContext.Out.WriteLine($"'{key}' in res.yml is never used in code.");
            }

            Assert.That(orphaned, Is.Empty,
                $"Found {orphaned.Count} orphaned response string(s) in res.yml");
        }

        [Test]
        public void AllLocaleStringsHaveSamePlaceholderCount()
        {
            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);
            var allStrings = stringsSource.GetResponseStrings();

            if (!allStrings.TryGetValue("en-US", out var enUs))
            {
                Assert.Fail("en-US response strings not found.");
                return;
            }

            var placeholderRegex = new Regex(@"\{(\d+)[}:]");
            int GetMaxPlaceholder(string val)
            {
                var matches = placeholderRegex.Matches(val);
                if (matches.Count == 0) return -1;
                var max = -1;
                foreach (Match m in matches)
                    max = Math.Max(max, int.Parse(m.Groups[1].Value));
                return max;
            }

            var isSuccess = true;
            foreach (var (locale, dict) in allStrings)
            {
                if (locale == "en-US") continue;
                foreach (var (key, val) in dict)
                {
                    if (!enUs.TryGetValue(key, out var enVal)) continue;
                    if (string.IsNullOrEmpty(val)) continue;

                    var expected = GetMaxPlaceholder(enVal);
                    var actual = GetMaxPlaceholder(val);
                    if (actual < expected)
                    {
                        TestContext.Out.WriteLine(
                            $"{locale}/{key}: expects {expected + 1} placeholder(s), has {actual + 1}");
                        isSuccess = false;
                    }
                }
            }

            Assert.That(isSuccess, Is.True,
                "Some locale strings are missing placeholders. See output for details.");
        }

        [Test]
        public void AllLocaleStringsHaveAllKeys()
        {
            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);
            var allStrings = stringsSource.GetResponseStrings();

            if (!allStrings.TryGetValue("en-US", out var enUs))
            {
                Assert.Fail("en-US response strings not found.");
                return;
            }

            var enUsKeys = enUs.Keys.ToHashSet();
            var isSuccess = true;

            foreach (var (locale, dict) in allStrings)
            {
                if (locale == "en-US") continue;

                var missing = enUsKeys.Except(dict.Keys).OrderBy(x => x).ToList();
                foreach (var key in missing)
                {
                    TestContext.Out.WriteLine($"{locale}: missing key '{key}'");
                    isSuccess = false;
                }
            }

            Assert.That(isSuccess, Is.True,
                "Some locale files are missing keys. See output for details.");
        }

        [Test]
        public void OnlySupportedLocalesHaveStrings()
        {
            var supported = SupportedLocales.All.Keys.ToHashSet();

            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);
            var allLocales = stringsSource.GetResponseStrings().Keys.ToHashSet();

            var unsupported = allLocales
                .Where(l => l != "en-US" && !supported.Contains(l))
                .OrderBy(x => x)
                .ToList();

            if (unsupported.Count > 0)
            {
                foreach (var locale in unsupported)
                    TestContext.Out.WriteLine(
                        $"'{locale}' has string files but is not in the supported locales list.");
            }

            Assert.That(unsupported, Is.Empty,
                $"Found {unsupported.Count} locale(s) with string files that are not in the supported locales list.");
        }
    }
}