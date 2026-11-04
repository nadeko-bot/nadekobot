#nullable enable
using System;
using NadekoBot.Modules.NadekoExpressions;
using NUnit.Framework;

namespace NadekoBot.Tests.Expressions;

public sealed class ExpressionWordPositionTests
{
    [Test]
    public void GetWordPosition_FindsTheFirstStandaloneWordInAnyCase()
    {
        (string Text, string Word, WordPosition Position, int Index)[] cases =
        [
            ("cats and a cat", "cat", WordPosition.End, 11),
            ("cats, CAT! dogs", "cat", WordPosition.Middle, 6),
            ("Hi there", "hi", WordPosition.Start, 0),
            ("say hi now", "HI", WordPosition.Middle, 4),
            ("concatenate", "cat", WordPosition.None, -1),
            ("cat", "cat", WordPosition.None, -1),
            ("no match", "cat", WordPosition.None, -1),
        ];

        foreach (var (text, word, position, index) in cases)
        {
            var actual = text.AsSpan().GetWordPosition(word, out var actualIndex);
            Assert.That((actual, actualIndex), Is.EqualTo((position, index)), $"'{word}' in '{text}'");
        }
    }

    [Test]
    public void GetTargetStart_PointsAfterTheTriggerInAnyCase()
    {
        (string Text, string Trigger, bool ContainsAnywhere, string Target)[] cases =
        [
            ("Say HI now", "hi", true, "now"),
            ("hi @someone", "hi", true, "@someone"),
            ("well, hi", "hi", true, ""),
            ("cats and a cat food", "cat", true, "food"),
            ("hug @someone", "hug", false, "@someone"),
            ("你好朋友", "你好", true, "朋友"),
            ("hi, @someone", "hi", true, "@someone"),
        ];

        foreach (var (text, trigger, ca, target) in cases)
        {
            var start = text.AsSpan().GetTargetStart(trigger, ca);
            Assert.That(text[start..].Trim(), Is.EqualTo(target), $"'{trigger}' in '{text}'");
        }
    }
}
