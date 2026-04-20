using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NadekoBot.Db;
using NUnit.Framework;

namespace NadekoBot.Tests;

public partial class ShardFilteredCoverageTests
{
    private static readonly Assembly _nadekoAssembly = typeof(NadekoContext).Assembly;

    [GeneratedRegex(@"Queries\.GuildOnShard<(\w+)>")]
    private static partial Regex GuildOnShardCallSiteRegex();

    [Test]
    public void AllShardFilteredEntitiesHaveGuildIdProperty()
    {
        var missing = _nadekoAssembly.GetTypes()
            .Where(static t => t.GetCustomAttribute<ShardFilteredAttribute>() is not null)
            .Where(static t => t.GetProperty("GuildId") is null)
            .Select(static t => t.Name)
            .ToList();

        Assert.That(missing, Is.Empty,
            $"[ShardFiltered] types without GuildId property: {string.Join(", ", missing)}");
    }

    [Test]
    public void AllGuildOnShardCallSiteTypesAreMarkedShardFiltered()
    {
        var srcDir = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "NadekoBot"));

        if (!Directory.Exists(srcDir))
            Assert.Inconclusive($"Source directory not found: {srcDir}");

        var csFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories);
        var regex = GuildOnShardCallSiteRegex();

        var referencedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in regex.Matches(content))
                referencedTypeNames.Add(m.Groups[1].Value);
        }

        var shardFilteredTypes = _nadekoAssembly.GetTypes()
            .Where(static t => t.GetCustomAttribute<ShardFilteredAttribute>() is not null)
            .Select(static t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missingAttribute = referencedTypeNames
            .Where(name => !shardFilteredTypes.Contains(name))
            .ToList();

        Assert.That(missingAttribute, Is.Empty,
            $"Types used in Queries.GuildOnShard<T> but missing [ShardFiltered]: {string.Join(", ", missingAttribute)}");
    }

    [Test]
    public void AllShardFilteredEntitiesAreUsedInGuildOnShard()
    {
        var srcDir = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "NadekoBot"));

        if (!Directory.Exists(srcDir))
            Assert.Inconclusive($"Source directory not found: {srcDir}");

        var csFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories);
        var regex = GuildOnShardCallSiteRegex();

        var referencedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match m in regex.Matches(content))
                referencedTypeNames.Add(m.Groups[1].Value);
        }

        var shardFilteredTypes = _nadekoAssembly.GetTypes()
            .Where(static t => t.GetCustomAttribute<ShardFilteredAttribute>() is not null)
            .Select(static t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unused = shardFilteredTypes
            .Where(name => !referencedTypeNames.Contains(name))
            .ToList();

        Assert.That(unused, Is.Empty,
            $"Types marked [ShardFiltered] but never used in Queries.GuildOnShard<T>: {string.Join(", ", unused)}");
    }
}
