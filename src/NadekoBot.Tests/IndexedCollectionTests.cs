using Nadeko.Common;
using NadekoBot.Db.Models;
using NUnit.Framework;
using System;
using System.Linq;

namespace NadekoBot.Tests
{
    public class IndexedCollectionTests
    {
        [Test]
        public void IndicesFollowThePositionAfterEveryChange()
        {
            var collection = new IndexedCollection<ShopEntry>(Enumerable.Range(0, 10).Select(x => new ShopEntry { Id = x }));

            var removed = collection[1];
            collection.Remove(removed);
            collection.RemoveAt(4);
            collection.Insert(2, new ShopEntry { Id = 222 });
            collection.Add(new ShopEntry { Id = 100 });
            collection[5] = new ShopEntry { Id = 555 };

            Assert.That(collection.Select(x => x.Id), Is.EqualTo(new[] { 0, 2, 222, 3, 4, 555, 7, 8, 9, 100 }));
            for (var index = 0; index < collection.Count; index++)
                Assert.That(collection[index].Index, Is.EqualTo(index));

            Assert.That(collection.Contains(removed), Is.False);
            Assert.That(collection.IndexOf(collection[3]), Is.EqualTo(3));

            Assert.Throws<ArgumentOutOfRangeException>(() => collection.RemoveAt(999));
            Assert.Throws<ArgumentOutOfRangeException>(() => collection.Insert(-3, new ShopEntry { Id = -3 }));

            collection.Clear();
            Assert.That(collection.Count, Is.EqualTo(0));
        }
    }
}
