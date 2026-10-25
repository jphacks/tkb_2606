using Cartograph.EditorUI.Navigation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class LinkMarkerTests
    {
        [Test]
        public void BuildLineWithoutKindIsPlainLinkAndIgnoresReason()
        {
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.None, "波動", null), Is.EqualTo("[[波動]]"));
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.None, "波動", "理由"), Is.EqualTo("[[波動]]"));
        }

        [Test]
        public void BuildLineUsesTheMarkerOfEachKind()
        {
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.Premise, "a", ""), Is.EqualTo("==> [[a]]"));
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.Contrast, "a", null), Is.EqualTo("<-> [[a]]"));
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.Example, "d/a", "例"), Is.EqualTo("..> [[d/a]]: 例"));
        }

        [Test]
        public void ReasonIsTrimmedAndKeptOnOneLine()
        {
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.Premise, "a", "  重ね\r\n合わせ  "), Is.EqualTo("==> [[a]]: 重ね  合わせ"));
            Assert.That(LinkMarker.BuildLine(LinkMarkerKind.Premise, "a", "   "), Is.EqualTo("==> [[a]]"));
        }

        [Test]
        public void ParseFindsKindOnlyWhenALinkFollows()
        {
            Assert.That(LinkMarker.Parse("==> [[a]]: x"), Is.EqualTo(LinkMarkerKind.Premise));
            Assert.That(LinkMarker.Parse("  <->[[a]]"), Is.EqualTo(LinkMarkerKind.Contrast));
            Assert.That(LinkMarker.Parse("\t..>  [[a]]"), Is.EqualTo(LinkMarkerKind.Example));
            Assert.That(LinkMarker.Parse("==> ただの文章"), Is.EqualTo(LinkMarkerKind.None));
            Assert.That(LinkMarker.Parse("[[a]]"), Is.EqualTo(LinkMarkerKind.None));
            Assert.That(LinkMarker.Parse("本文 ==> [[a]]"), Is.EqualTo(LinkMarkerKind.None));
            Assert.That(LinkMarker.Parse(null), Is.EqualTo(LinkMarkerKind.None));
        }

        [Test]
        public void PrefixLengthCoversMarkerAndBlanksSoDisplayStartsAtTheLink()
        {
            string line = "==>  [[a]]: x";
            Assert.That(LinkMarker.PrefixLength(line, out var kind), Is.EqualTo(5));
            Assert.That(kind, Is.EqualTo(LinkMarkerKind.Premise));
            Assert.That(line.Substring(5), Is.EqualTo("[[a]]: x"));
            Assert.That(LinkMarker.PrefixLength("普通の行", out kind), Is.EqualTo(0));
            Assert.That(kind, Is.EqualTo(LinkMarkerKind.None));
        }

        [Test]
        public void BuiltLinesParseBackToTheirKind()
        {
            foreach (var kind in new[] { LinkMarkerKind.Premise, LinkMarkerKind.Contrast, LinkMarkerKind.Example })
                Assert.That(LinkMarker.Parse(LinkMarker.BuildLine(kind, "x", "r")), Is.EqualTo(kind));
        }
    }
}
