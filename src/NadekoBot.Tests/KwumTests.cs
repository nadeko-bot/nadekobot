using System;
using Nadeko.Common;
using NUnit.Framework;

namespace NadekoBot.Tests
{
    public class KwumTests
    {
        [Test]
        public void TestDefaultHashCode()
        {
            var num = default(kwum);

            Assert.That(0, Is.EqualTo(num.GetHashCode()));
        }
        
        [Test]
        public void TestEqualGetHashCode()
        {
            var num1 = new kwum("234");
            var num2 = new kwum("234");

            Assert.That(num1.GetHashCode(), Is.EqualTo(num2.GetHashCode()));
        }
        
        [Test]
        public void TestNotEqualGetHashCode()
        {
            var num1 = new kwum("234");
            var num2 = new kwum("235");

            Assert.That(num1.GetHashCode(), Is.Not.EqualTo(num2.GetHashCode()));
        }
        
        [Test]
        public void TestLongEqualGetHashCode()
        {
            var num1 = new kwum("hgbkhdbk");
            var num2 = new kwum("hgbkhdbk");

            Assert.That(num1.GetHashCode(), Is.EqualTo(num2.GetHashCode()));
        }
        
        [Test]
        public void TestEqual()
        {
            var num1 = new kwum("hgbkhd");
            var num2 = new kwum("hgbkhd");

            Assert.That(num1, Is.EqualTo(num2));
        }
        
        [Test]
        public void TestNotEqual()
        {
            var num1 = new kwum("hgbk5d");
            var num2 = new kwum("hgbk4d");

            Assert.That(num1, Is.Not.EqualTo(num2));
        }
        
        [Test]
        public void TestParseValidValue()
        {
            var validValue = "234e";

            Assert.That(kwum.TryParse(validValue, out _), Is.True);
        }

        [Test]
        public void TestParseInvalidValue()
        {
            var invalidValue = "1234";
            Assert.That(kwum.TryParse(invalidValue, out _), Is.False);
        }
        
        [Test]
        public void TestCorrectParseValue()
        {
            var validValue = "qwerf4bm";
            kwum.TryParse(validValue, out var parsedValue);
            
            Assert.That(parsedValue, Is.EqualTo(new kwum(validValue)));
        }
        
        [Test]
        public void TestToString()
        {
            var validValue = "46g5yh";
            kwum.TryParse(validValue, out var parsedValue);
            
            Assert.That(validValue, Is.EqualTo(parsedValue.ToString()));
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
        }

        [Test]
        public void TestMaxValue()
        {
            var num = new kwum(int.MaxValue - 1);
            Assert.That("3zzzzzy", Is.EqualTo(num.ToString()));
            
            num = new kwum(int.MaxValue);
            Assert.That("3zzzzzz", Is.EqualTo(num.ToString()));
        }

        [Test]
        public void TestPower()
        {
            var num = new kwum((int)Math.Pow(32, 2));
            Assert.That("322", Is.EqualTo(num.ToString()));
        }
    }
}