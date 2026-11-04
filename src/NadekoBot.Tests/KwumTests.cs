using Nadeko.Common;
using NUnit.Framework;

namespace NadekoBot.Tests
{
    public class KwumTests
    {
        [Test]
        public void ConvertsParsesAndCompares()
        {
            Assert.That(new kwum(10).ToString(), Is.EqualTo("c"));
            Assert.That(new kwum(123).ToString(), Is.EqualTo("5v"));
            Assert.That(new kwum(int.MaxValue).ToString(), Is.EqualTo("3zzzzzz"));
            Assert.That((int)new kwum(10), Is.EqualTo(10));

            // leading zeros have no meaning
            Assert.That(new kwum("22225v"), Is.EqualTo(new kwum(123)));

            Assert.That(new kwum("234").GetHashCode(), Is.EqualTo(new kwum("234").GetHashCode()));
            Assert.That(new kwum("hgbk5d"), Is.Not.EqualTo(new kwum("hgbk4d")));

            Assert.That(kwum.TryParse("46g5yh", out var parsed), Is.True);
            Assert.That(parsed.ToString(), Is.EqualTo("46g5yh"));
            Assert.That(kwum.TryParse("1234", out _), Is.False);
        }
    }
}
