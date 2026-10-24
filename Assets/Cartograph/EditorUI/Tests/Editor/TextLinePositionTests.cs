using System;
using Cartograph.EditorUI.Navigation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class TextLinePositionTests
    {
        [Test]
        public void JapaneseEmojiAndCrLfUseUtf16Offsets()
        {
            Assert.That(TextLinePosition.Offset("😀あ\r\n## 見出し\n本文", 2), Is.EqualTo(5));
        }
        [Test]
        public void EmptyFirstLineAndOutOfRangeAreClamped()
        {
            Assert.That(TextLinePosition.Offset(null, 1), Is.EqualTo(0));
            Assert.That(TextLinePosition.Offset("abc", 99), Is.EqualTo(3));
            Assert.That(TextLinePosition.Offset("a\n", 2), Is.EqualTo(2));
        }
        [Test]
        public void AllNewlineStylesResolveHeadingPosition()
        {
            Assert.That(TextLinePosition.Offset("a\nb", 2), Is.EqualTo(2));
            Assert.That(TextLinePosition.Offset("a\rb", 2), Is.EqualTo(2));
            Assert.That(TextLinePosition.Offset("a\r\nb", 2), Is.EqualTo(3));
        }
        [Test]
        public void ZeroAndNegativeLineNumbersAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TextLinePosition.Offset("text", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TextLinePosition.Offset("text", -1));
        }
    }
}
