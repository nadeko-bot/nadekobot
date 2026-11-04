using System;
using System.Collections.Generic;
using System.Reflection;
using NadekoBot.Modules.Permissions.Services;
using NUnit.Framework;

namespace NadekoBot.Tests;

[TestFixture]
public sealed class FilteredWordSetTests
{
    private static object CreateFilteredWordSet()
    {
        var type = typeof(FilterService).GetNestedType("FilteredWordSet", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type)!;
    }

    private static HashSet<string> GetSnapshot(object fws)
        => (HashSet<string>)fws.GetType().GetProperty("Snapshot")!.GetValue(fws)!;

    private static bool InvokeAdd(object fws, string word)
        => (bool)fws.GetType().GetMethod("Add")!.Invoke(fws, [word])!;

    private static bool InvokeRemove(object fws, string word)
        => (bool)fws.GetType().GetMethod("Remove")!.Invoke(fws, [word])!;

    private static void InvokeClear(object fws)
        => fws.GetType().GetMethod("Clear")!.Invoke(fws, []);

    [Test]
    public void Snapshot_FollowsAddRemoveAndClear_CaseInsensitive()
    {
        var fws = CreateFilteredWordSet();

        Assert.That(InvokeAdd(fws, "BadWord"), Is.True);
        InvokeAdd(fws, "hello");

        var snapshot = GetSnapshot(fws);
        Assert.That(snapshot.Contains("badword"), Is.True);
        Assert.That(snapshot.Contains("BADWORD"), Is.True);

        var lookup = snapshot.GetAlternateLookup<ReadOnlySpan<char>>();
        Assert.That(lookup.Contains("say badword now".AsSpan(4, 7)), Is.True);
        Assert.That(lookup.Contains("goodword".AsSpan()), Is.False);

        Assert.That(InvokeRemove(fws, "hello"), Is.True);

        snapshot = GetSnapshot(fws);
        Assert.That(snapshot.Contains("hello"), Is.False);
        Assert.That(snapshot.Contains("badword"), Is.True);

        InvokeClear(fws);
        Assert.That(GetSnapshot(fws), Is.Empty);
    }
}
