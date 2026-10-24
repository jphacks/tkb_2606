using System.Linq;
using NUnit.Framework;
using Cartograph.EditorUI;

public sealed class RelatedLinkRemovalTests
{
    [Test]
    public async System.Threading.Tasks.Task DisconnectRemovesBothDirectionsAndRetainsTypedContent()
    {
        var env = new Cartograph.Core.Tests.Env(); env.PutNote("A", "# A\n[[B]]\n本文"); env.PutNote("B", "# B\n[[A]]");
        var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
        var before = source.Current;
        source.ReviewDocument("A", before.revision, Cartograph.Core.Tests.Typing.Key(before.markdown + "字", before.markdown.Length, "字"));
        await RelatedLinkRemoval.Disconnect(source, "A", "B"); await source.FlushSave();
        Assert.That(env.Body("A"), Is.EqualTo("# A\n\n本文字"));
        Assert.That(env.Body("B"), Is.EqualTo("# B\n"));
        Assert.That(source.Current.id, Is.EqualTo("A"));
        Assert.That(source.GetLabeledDocument("A").TypedCount(), Is.EqualTo(1));
    }
    [Test]
    public void RemovesOnlyLinksNotSurroundingReasonsOrOtherTargets()
    {
        string text = "# source\r\n==> [[target#見出し]]: 理由 [[other]]\r\n[[ target ]]";
        var ranges = RelatedLinkRemoval.GetRanges(text, "source", "target", new[] { "source", "target", "other" });
        Assert.That(ranges.Count, Is.EqualTo(2));
        Assert.That(ranges[0].Start, Is.GreaterThan(ranges[1].Start));
        foreach (var range in ranges) text = text.Remove(range.Start, range.Length);
        Assert.That(text, Is.EqualTo("# source\r\n==> : 理由 [[other]]\r\n"));
    }
    [Test]
    public void SkipsFencedCodeAndHeadingLinksLikeSharedParser()
    {
        string text = "# [[target]]\n```\n[[target]]\n```\n本文 [[target]]";
        var range = RelatedLinkRemoval.GetRanges(text, "source", "target", new[] { "source", "target" });
        Assert.That(range.Count, Is.EqualTo(1));
        Assert.That(range[0].Start, Is.EqualTo(text.LastIndexOf("[[target]]", System.StringComparison.Ordinal)));
    }
    [Test]
    public void AmbiguousBareNameIsNotDeletedButExactFolderPathIs()
    {
        string text = "[[target]] [[one/target#見出し]] [[two/target]]";
        var ranges = RelatedLinkRemoval.GetRanges(text, "source", "one/target", new[] { "source", "one/target", "two/target" });
        Assert.That(ranges.Count, Is.EqualTo(1));
        Assert.That(text.Substring(ranges[0].Start, ranges[0].Length), Is.EqualTo("[[one/target#見出し]]"));
    }
    [Test]
    public void SameNoteHeadingLinksResolveToCurrentNote()
    {
        var ranges = RelatedLinkRemoval.GetRanges("# one\r[[#one]] [[other#one]]", "source", "source", new[] { "source", "other" });
        Assert.That(ranges.Count, Is.EqualTo(1));
        Assert.That(ranges[0].Start, Is.EqualTo(6));
    }
    [Test]
    public void UnknownTargetAndOrdinaryProseAreUntouched()
    {
        Assert.That(RelatedLinkRemoval.GetRanges("target [[missing]]", "source", "target", new[] { "source" }), Is.Empty);
        Assert.That(RelatedLinkRemoval.GetRanges("target", "source", "target", new[] { "source", "target" }), Is.Empty);
    }
}
