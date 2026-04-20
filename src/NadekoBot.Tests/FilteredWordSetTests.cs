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
    public void Add_SnapshotContainsWord_CaseInsensitive()
    {
        var fws = CreateFilteredWordSet();

        Assert.That(InvokeAdd(fws, "BadWord"), Is.True);

        var snapshot = GetSnapshot(fws);
        Assert.That(snapshot.Contains("badword"), Is.True);
        Assert.That(snapshot.Contains("BADWORD"), Is.True);
    }

    [Test]
    public void Remove_SnapshotDoesNotContainWord()
    {
        var fws = CreateFilteredWordSet();
        InvokeAdd(fws, "hello");
        InvokeAdd(fws, "world");

        Assert.That(InvokeRemove(fws, "hello"), Is.True);

        var snapshot = GetSnapshot(fws);
        Assert.That(snapshot.Contains("hello"), Is.False);
        Assert.That(snapshot.Contains("world"), Is.True);
    }

    [Test]
    public void Clear_SnapshotIsEmpty()
    {
        var fws = CreateFilteredWordSet();
        InvokeAdd(fws, "a");
        InvokeAdd(fws, "b");

        InvokeClear(fws);

        var snapshot = GetSnapshot(fws);
        Assert.That(snapshot, Is.Empty);
    }
}
