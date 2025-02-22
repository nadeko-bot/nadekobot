using System.Collections.Generic;
using NUnit.Framework;

namespace NadekoBot.Tests;

public class ConcurrentHashSetTests
{
    private ConcurrentHashSet<(int?, int?)> _set;

    [SetUp]
    public void SetUp()
    {
        _set = new();
    }

    [Test]
    public void AddTest()
    {
        var result = _set.Add((1, 2));
        
        Assert.That(result, Is.EqualTo(true));
        
        result = _set.Add((1, 2));
        
        Assert.That(result, Is.EqualTo(false));
    }

    [Test]
    public void TryRemoveTest()
    {
        _set.Add((1, 2));
        var result = _set.TryRemove((1, 2));
        
        Assert.That(result, Is.EqualTo(true));
        
        result = _set.TryRemove((1, 2));
        Assert.That(result, Is.EqualTo(false));
    }

    [Test]
    public void CountTest()
    {
        _set.Add((1, 2)); // 1
        _set.Add((1, 2)); // 1
        
        _set.Add((2, 2)); // 2
        
        _set.Add((3, 2)); // 3
        _set.Add((3, 2)); // 3
         
         Assert.That(_set.Count, Is.EqualTo(3));
    }

    [Test]
    public void ClearTest()
    {
        _set.Add((1, 2));
        _set.Add((1, 3));
        _set.Add((1, 4));
        
        _set.Clear();
        
        Assert.That(_set.Count, Is.EqualTo(0));
    }

    [Test]
    public void ContainsTest()
    {
        _set.Add((1, 2));
        _set.Add((3, 2));
        
        Assert.That(_set.Contains((1, 2)), Is.EqualTo(true));
        Assert.That(_set.Contains((3, 2)), Is.EqualTo(true));
        Assert.That(_set.Contains((2, 1)), Is.EqualTo(false));
        Assert.That(_set.Contains((2, 3)), Is.EqualTo(false));
    }

    [Test]
    public void RemoveWhereTest()
    {
        _set.Add((1, 2));
        _set.Add((1, 3));
        _set.Add((1, 4));
        _set.Add((2, 5));
        
        // remove tuples which have even second item
        _set.RemoveWhere(static x => x.Item2 % 2 == 0);
        
        Assert.That(_set.Count, Is.EqualTo(2));
        Assert.That(_set.Contains((1, 3)), Is.EqualTo(true));
        Assert.That(_set.Contains((2, 5)), Is.EqualTo(true));
    }
}