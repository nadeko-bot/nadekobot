using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Discord;
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
        public void CommandMethodsAliasesAndCommandStringsMatch()
        {
            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);
            var strings = new MemoryBotStringsProvider(stringsSource);
            var allAliases = CommandNameLoadHelper.LoadAliases(aliasesPath);
            var methodNames = GetCommandMethodNames().ToHashSet();

            var missingAlias = methodNames.Where(m => !allAliases.ContainsKey(m)).ToList();
            var missingStrings = allAliases.Keys.Where(m => strings.GetCommandStrings("en-US", m) is null).ToList();
            var obsoleteStrings = stringsSource.GetCommandStrings()["en-US"].Keys
                .Where(c => !methodNames.Contains(c))
                .ToList();
            var obsoleteAliases = allAliases.Keys.Where(m => !methodNames.Contains(m)).ToList();

            // the help embed shows the description in one field, and Discord refuses a longer field value
            var tooLong = new List<string>();
            foreach (var (locale, commands) in stringsSource.GetCommandStrings())
            {
                foreach (var (name, cmd) in commands)
                {
                    if (cmd.Desc is { Length: > EmbedFieldBuilder.MaxFieldValueLength })
                        tooLong.Add($"{locale} {name}: {cmd.Desc.Length}");
                }
            }

            if (obsoleteAliases.Count > 0)
                Assert.Warn($"Entries in strings/names.yml without a command method: {string.Join(", ", obsoleteAliases)}");

            Assert.Multiple(() =>
            {
                Assert.That(missingAlias, Is.Empty, "Command methods without an alias in strings/names.yml");
                Assert.That(missingStrings, Is.Empty, "Commands without strings in cmds.yml");
                Assert.That(obsoleteStrings, Is.Empty, "Command strings without a command method");
                Assert.That(tooLong, Is.Empty, "Command descriptions longer than an embed field");
            });
        }

        [Test]
        public void NoOrphanedResponseStrings()
        {
            var stringsSource = new LocalFileStringsSource(responsesPath, commandsPath);
            var allKeys = stringsSource.GetResponseStrings()["en-US"].Keys.ToHashSet();

            var usedKeys = new HashSet<string>();
            var srcPath = Path.GetFullPath(Path.Combine("../../../..", "NadekoBot"));
            var csFiles = Directory.GetFiles(srcPath, "*.cs", SearchOption.AllDirectories);
            var strsPattern = new Regex(@"strs\.(\w+)");

            foreach (var file in csFiles)
            {
                var content = File.ReadAllText(file);
                foreach (Match match in strsPattern.Matches(content))
                    usedKeys.Add(match.Groups[1].Value);
            }

            var orphaned = allKeys.Except(usedKeys).OrderBy(x => x).ToList();
            foreach (var key in orphaned)
                TestContext.Out.WriteLine($"'{key}' in res.yml is never used in code.");

            Assert.That(orphaned, Is.Empty,
                $"Found {orphaned.Count} orphaned response string(s) in res.yml");
        }

        [Test]
        public void LocaleStringsAreSupportedAndMatchEnglishKeysAndPlaceholders()
        {
            var allStrings = new LocalFileStringsSource(responsesPath, commandsPath).GetResponseStrings();
            Assert.That(allStrings.TryGetValue("en-US", out var enUs), Is.True, "en-US response strings not found.");

            var supported = SupportedLocales.All.Keys.ToHashSet();
            var placeholderRegex = new Regex(@"\{(\d+)[}:]");
            int GetMaxPlaceholder(string val)
            {
                var max = -1;
                foreach (Match m in placeholderRegex.Matches(val))
                    max = Math.Max(max, int.Parse(m.Groups[1].Value));
                return max;
            }

            var problems = new List<string>();
            foreach (var (locale, dict) in allStrings)
            {
                if (locale == "en-US")
                    continue;

                if (!supported.Contains(locale))
                    problems.Add($"'{locale}' has string files but is not in the supported locales list.");

                foreach (var key in enUs!.Keys)
                {
                    if (!dict.ContainsKey(key))
                        problems.Add($"{locale}: missing key '{key}'");
                }

                foreach (var (key, val) in dict)
                {
                    if (!enUs.TryGetValue(key, out var enVal) || string.IsNullOrEmpty(val))
                        continue;

                    var expected = GetMaxPlaceholder(enVal);
                    var actual = GetMaxPlaceholder(val);
                    if (actual < expected)
                        problems.Add($"{locale}/{key}: expects {expected + 1} placeholder(s), has {actual + 1}");
                }
            }

            foreach (var problem in problems)
                TestContext.Out.WriteLine(problem);

            Assert.That(problems, Is.Empty, "Some locale strings are wrong. See output for details.");
        }
    }
}
