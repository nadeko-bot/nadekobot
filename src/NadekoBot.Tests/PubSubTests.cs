using System.Collections.Generic;
using System.Threading.Tasks;
using Nadeko.Common;
using NUnit.Framework;

namespace NadekoBot.Tests
{
    public class PubSubTests
    {
        [Test]
        public async Task EventPubSub_DeliversToEachSubscriptionUntilItsOwnUnsub()
        {
            TypedKey<object> key = "test_key";
            var pubsub = new EventPubSub();
            var data = new object();
            var received = new List<(string, object)>();

            ValueTask First(object d)
            {
                received.Add(("first", d));
                return default;
            }

            ValueTask Second(object d)
            {
                received.Add(("second", d));
                return default;
            }

            await pubsub.Sub(key, First);
            await pubsub.Sub(key, Second);
            await pubsub.Sub(key, Second);
            await pubsub.Unsub(key, Second);

            await pubsub.Unsub(key, _ => default);
            await pubsub.Unsub(key, d =>
            {
                received.Add(("first", d));
                return default;
            });

            await pubsub.Pub(key, data);
            Assert.That(received, Is.EquivalentTo(new[] { ("first", data), ("second", data) }));

            received.Clear();
            await pubsub.Unsub(key, First);
            await pubsub.Unsub(key, Second);
            await pubsub.Pub(key, data);
            Assert.That(received, Is.Empty);
        }
    }
}
