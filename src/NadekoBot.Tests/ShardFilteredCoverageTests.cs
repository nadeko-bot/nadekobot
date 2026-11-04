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

    [GeneratedRegex(@"(?:/\s*4194304|>>\s*22|Math\.Pow\s*\(\s*2\s*,\s*22\s*\))")]
    private static partial Regex RawShardMathRegex();

    private static string GetSourceDir()
    {
        var srcDir = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "NadekoBot"));

        if (!Directory.Exists(srcDir))
            Assert.Inconclusive($"Source directory not found: {srcDir}");

        return srcDir;
    }

    [Test]
    public void ShardFilteredEntitiesHaveGuildIdAndMatchGuildOnShardCallSites()
    {
        var shardFiltered = _nadekoAssembly.GetTypes()
            .Where(static t => t.GetCustomAttribute<ShardFilteredAttribute>() is not null)
            .ToList();

        var missingGuildId = shardFiltered
            .Where(static t => t.GetProperty("GuildId") is null)
            .Select(static t => t.Name)
            .ToList();

        Assert.That(missingGuildId, Is.Empty,
            $"[ShardFiltered] types without GuildId property: {string.Join(", ", missingGuildId)}");

        var regex = GuildOnShardCallSiteRegex();
        var referencedTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(GetSourceDir(), "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            foreach (Match m in regex.Matches(content))
                referencedTypeNames.Add(m.Groups[1].Value);
        }

        var shardFilteredNames = shardFiltered
            .Select(static t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missingAttribute = referencedTypeNames.Where(name => !shardFilteredNames.Contains(name)).ToList();
        var unused = shardFilteredNames.Where(name => !referencedTypeNames.Contains(name)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(missingAttribute, Is.Empty,
                $"Types used in Queries.GuildOnShard<T> but missing [ShardFiltered]: {string.Join(", ", missingAttribute)}");
            Assert.That(unused, Is.Empty,
                $"Types marked [ShardFiltered] but never used in Queries.GuildOnShard<T>: {string.Join(", ", unused)}");
        });
    }

    [Test]
    public void NoRawShardMathOutsideAllowedFiles()
    {
        var srcDir = GetSourceDir();

        // Files allowed to use raw shard-snowflake math:
        // - Linq2DbExpressions.cs: canonical form of Queries.GuildOnShard<T>
        // - ShardIndexReconciler.cs: emits the CREATE INDEX SQL
        // - SelfService.cs: in-memory filter over already-loaded AutoCommand list
        // - BlacklistService.cs: non-GuildId shard math (ItemId), already SQL-pushed
        // - InfoCommands.cs: uses `>> 22` to decode Discord snowflake creation timestamps (unrelated to sharding)
        // - FishService.cs: uses `>> 22` as part of a channel-id RNG hash (unrelated to sharding)
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Linq2DbExpressions.cs",
            "ShardIndexReconciler.cs",
            "SelfService.cs",
            "BlacklistService.cs",
            "InfoCommands.cs",
            "FishService.cs",
        };

        var regex = RawShardMathRegex();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (allowed.Contains(fileName))
                continue;

            var content = File.ReadAllText(file);
            if (regex.IsMatch(content))
                violations.Add(Path.GetRelativePath(srcDir, file));
        }

        Assert.That(violations, Is.Empty,
            "Raw shard math (/ 4194304, >> 22, Math.Pow(2,22)) found outside the allowed files. "
            + $"Use Queries.GuildOnShard<T> instead. Offenders: {string.Join(", ", violations)}");
    }
}
