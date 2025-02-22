using System;
using System.Text;
using NadekoBot.Common.Yml;
using NUnit.Framework;

namespace NadekoBot.Tests
{
    public class RandomTests
    {
        [SetUp]
        public void Setup()
            => Console.OutputEncoding = Encoding.UTF8;

        [Test]
        public void Utf8CodepointsToEmoji()
        {
            var point = @"0001F338";
            var hopefullyEmoji = YamlHelper.UnescapeUnicodeCodePoint(point);

            Assert.That("🌸", Is.EqualTo(hopefullyEmoji), "Yaml unescape doesn't work properly.");
        }
    }
}