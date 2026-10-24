using NUnit.Framework;
using Cartograph.EditorUI;

public sealed class MarkdownBlockProjectionTests
{
    [Test]
    public void HeadingAndListPrefixesAreHiddenWithSourceOffsetsPreserved()
    {
        var blocks = MarkdownBlockProjection.Parse("## 見出し\r\n- 本文\n");
        Assert.That(blocks.Count, Is.EqualTo(3));
        Assert.That(blocks[0].HeadingLevel, Is.EqualTo(2));
        Assert.That(blocks[0].DisplayText, Is.EqualTo("見出し"));
        Assert.That(blocks[0].RawOffset(0), Is.EqualTo(3));
        Assert.That(blocks[1].SourceStart, Is.EqualTo(8));
        Assert.That(blocks[1].DisplayText, Is.EqualTo("本文"));
        Assert.That(blocks[2].DisplayText, Is.Empty);
    }
    [Test]
    public void VisibleEditsPreserveHeadingAndInlineFormatting()
    {
        var block = MarkdownBlockProjection.Parse("## **日本語**")[0];
        Assert.That(block.DisplayText, Is.EqualTo("日本語"));
        Assert.That(block.ReplaceVisible("日本の言葉"), Is.EqualTo("## **日本の言葉**"));
        Assert.That(block.ReplaceVisible(""), Is.EqualTo("## "));
        Assert.That(block.ReplaceVisible(block.DisplayText), Is.EqualTo(block.Raw));
    }
    [Test]
    public void LinkLabelsRemainEditableWithoutChangingTargets()
    {
        var markdown = MarkdownBlockProjection.Parse("[説明](other.md)")[0];
        Assert.That(markdown.DisplayText, Is.EqualTo("説明"));
        Assert.That(markdown.ReplaceVisible("説明文"), Is.EqualTo("[説明文](other.md)"));
        var wiki = MarkdownBlockProjection.Parse("[[other#見出し|関連記事]]")[0];
        Assert.That(wiki.DisplayText, Is.EqualTo("関連記事"));
        Assert.That(wiki.ReplaceVisible("参照"), Is.EqualTo("[[other#見出し|参照]]"));
    }
    [Test]
    public void LiteralCodeAndEscapedMarkupRemainVisible()
    {
        Assert.That(MarkdownBlockProjection.Parse("`**code**` と \\*文字\\*")[0].DisplayText,
            Is.EqualTo("**code** と *文字*"));
        Assert.That(MarkdownBlockProjection.Parse("価格 * 2")[0].DisplayText, Is.EqualTo("価格 * 2"));
        Assert.That(MarkdownBlockProjection.Parse("### ")[0].ReplaceVisible("見出し"), Is.EqualTo("### 見出し"));
    }
    [Test]
    public void EmojiOffsetsUseUtf16AndEditsDoNotSplitSurrogatePairs()
    {
        var block = MarkdownBlockProjection.Parse("# あ😀い")[0];
        Assert.That(block.RawOffset(3), Is.EqualTo(5));
        Assert.That(block.ReplaceVisible("あ🌸い"), Is.EqualTo("# あ🌸い"));
    }
    [Test]
    public void ReplacementAcrossFormattingRunsDoesNotLeaveUnmatchedMarkers()
    {
        var block = MarkdownBlockProjection.Parse("## **one** and *two*")[0];
        string replacement = block.ReplaceVisible("other and three");
        Assert.That(MarkdownBlockProjection.Parse(replacement)[0].DisplayText, Is.EqualTo("other and three"));
    }
    [TestCase(1, 1)]
    [TestCase(4, 5)]
    [TestCase(9, 10)]
    public void ExternalEditRemapsSelectionWithoutUnconditionallyCollapsingIt(int before, int after)
    {
        Assert.That(MarkdownBlockProjection.MapSelectionOffset(before, new Cartograph.Core.DiffResult(2, "ab", "XYZ")), Is.EqualTo(after));
    }
}
