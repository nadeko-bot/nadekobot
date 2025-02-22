using NUnit.Framework;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Discord.Commands;
using NadekoBot.Common;
using NadekoBot.Common.Attributes;
using NadekoBot.Services;


namespace NadekoBot.Tests
{
    public class CommandStringsTests
    {
        private const string responsesPath = "../../../../NadekoBot/strings/responses";
        private const string commandsPath = "../../../../NadekoBot/strings/commands";
        private const string aliasesPath = "../../../../NadekoBot/strings/aliases.yml";

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
                    TestContext.Out.WriteLine($"{methodName} doesn't exist in commands.en-US.yml");
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
                    TestContext.WriteLine($"'{methodName}' from aliases.yml doesn't have a matching command method.");
                    isSuccess = false;
                }
            }

            if (isSuccess)
                Assert.Pass();
            else
                Assert.Warn("There are some unused entries in strings/aliases.yml");
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
    }
}