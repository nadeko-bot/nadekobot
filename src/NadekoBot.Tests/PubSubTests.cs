using System.Threading.Tasks;
using Nadeko.Common;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace NadekoBot.Tests
{
    public class PubSubTests
    {
        [Test]
        public async Task Test_EventPubSub_PubSub()
        {
            TypedKey<int> key = "test_key";
            var expected = new Randomizer().Next();
            var pubsub = new EventPubSub();
            await pubsub.Sub(key, data =>
            {
                Assert.That(expected, Is.EqualTo(data));
                Assert.Pass();
                return default;
            });
            await pubsub.Pub(key, expected);
            Assert.Fail("Event not registered");
        }
        
        [Test]
        public async Task Test_EventPubSub_MeaninglessUnsub()
        {
            TypedKey<int> key = "test_key";
            var expected = new Randomizer().Next();
            var pubsub = new EventPubSub();
            await pubsub.Sub(key, data =>
            {
                Assert.That(expected, Is.EqualTo(data));
                Assert.Pass();
                return default;
            });
            await pubsub.Unsub(key, _ => default);
            await pubsub.Pub(key, expected);
            Assert.Fail("Event not registered");
        }
        
        [Test]
        public async Task Test_EventPubSub_MeaninglessUnsubThatLooksTheSame()
        {
            TypedKey<int> key = "test_key";
            var expected = new Randomizer().Next();
            var pubsub = new EventPubSub();
            await pubsub.Sub(key, data =>
            {
                Assert.That(expected, Is.EqualTo(data));
                Assert.Pass();
                return default;
            });
            await pubsub.Unsub(key, data =>
            {
                Assert.That(expected, Is.EqualTo(data));
                Assert.Pass();
                return default;
            });
            await pubsub.Pub(key, expected);
            Assert.Fail("Event not registered");
        }
        
        [Test]
        public async Task Test_EventPubSub_MeaningfullUnsub()
        {
            TypedKey<int> key = "test_key";
            var pubsub = new EventPubSub();

            ValueTask Action(int data)
            {
                Assert.Fail("Event is raised when it shouldn't be");
                return default;
            }

            await pubsub.Sub(key, Action);
            await pubsub.Unsub(key, Action);
            await pubsub.Pub(key, 0);
            Assert.Pass();
        }
        
        [Test]
        public async Task Test_EventPubSub_ObjectData()
        {
            TypedKey<byte[]> key = "test_key";
            var pubsub = new EventPubSub();

            var localData = new byte[1]; 
            
            ValueTask Action(byte[] data)
            {
                Assert.That(localData, Is.EqualTo(data));
                Assert.Pass();
                return default;
            }

            await pubsub.Sub(key, Action);
            await pubsub.Pub(key, localData);
            
            Assert.Fail("Event not raised");
        }
        
        [Test]
        public async Task Test_EventPubSub_MultiSubUnsub()
        {
            TypedKey<object> key = "test_key";
            var pubsub = new EventPubSub();

            var localData = new object();
            int successCounter = 0;
            
            ValueTask Action1(object data)
            {
                Assert.That(localData, Is.EqualTo(data));
                successCounter+=10;
                return default;
            }
            
            ValueTask Action2(object data)
            {
                Assert.That(localData, Is.EqualTo(data));
                successCounter++;
                return default;
            }

            await pubsub.Sub(key, Action1); // + 10 \
            await pubsub.Sub(key, Action2); // + 1 - + = 12
            await pubsub.Sub(key, Action2); // + 1 /
            await pubsub.Unsub(key, Action2); // - 1/
            await pubsub.Pub(key, localData);
            
            Assert.That(successCounter, Is.EqualTo(11), "Not all events are raised.");
        }
    }
}