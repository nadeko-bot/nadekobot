using System;
using Nadeko.Common;
using NUnit.Framework;

namespace NadekoBot.Tests
{
    public class KwumTests
    {
        [Test]
        public void TestGetHashCode()
        {
            Assert.That(0, Is.EqualTo(default(kwum).GetHashCode()));
            Assert.That(new kwum("234").GetHashCode(), Is.EqualTo(new kwum("234").GetHashCode()));
            Assert.That(new kwum("234").GetHashCode(), Is.Not.EqualTo(new kwum("235").GetHashCode()));
            Assert.That(new kwum("hgbkhdbk").GetHashCode(), Is.EqualTo(new kwum("hgbkhdbk").GetHashCode()));
        }
        
        [Test]
        public void TestEquality()
        {
            Assert.That(new kwum("hgbkhd"), Is.EqualTo(new kwum("hgbkhd")));
            Assert.That(new kwum("hgbk5d"), Is.Not.EqualTo(new kwum("hgbk4d")));
        }
        
        [Test]
        public void TestParse()
        {
            Assert.That(kwum.TryParse("234e", out _), Is.True);
            Assert.That(kwum.TryParse("1234", out _), Is.False);

            var validValue = "qwerf4bm";
            Assert.That(kwum.TryParse(validValue, out var parsedValue), Is.True);
            Assert.That(parsedValue, Is.EqualTo(new kwum(validValue)));

            Assert.That(kwum.TryParse("46g5yh", out var roundTrip), Is.True);
            Assert.That(roundTrip.ToString(), Is.EqualTo("46g5yh"));
        }

        [Test]
        public void TestConversionsToFromInt()
        {
            var num = new kwum(10);
            
            Assert.That(10, Is.EqualTo((int)num));
            Assert.That(num, Is.EqualTo((kwum)10));
        }

        [Test]
        public void TestConverstionsToString()
        {
            var num = new kwum(10);
            Assert.That("c", Is.EqualTo(num.ToString()));
            num = new kwum(123);
            Assert.That("5v", Is.EqualTo(num.ToString()));
            
            // leading zeros have no meaning
            Assert.That(new kwum("22225v"), Is.EqualTo(num));

            Assert.That("322", Is.EqualTo(new kwum((int)Math.Pow(32, 2)).ToString()));
        }

        [Test]
        public void TestMaxValue()
        {
            var num = new kwum(int.MaxValue - 1);
            Assert.That("3zzzzzy", Is.EqualTo(num.ToString()));
            
            num = new kwum(int.MaxValue);
            Assert.That("3zzzzzz", Is.EqualTo(num.ToString()));
        }
    }
}