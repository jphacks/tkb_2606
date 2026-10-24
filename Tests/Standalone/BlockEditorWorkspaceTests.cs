using System;
using System.Threading.Tasks;
using Cartograph.Core;
using Cartograph.Core.Tests;
using Cartograph.EditorUI;
using NUnit.Framework;

namespace Cartograph.IntegrationTests
{
    public sealed class BlockEditorWorkspaceTests
    {
        [Test]
        public async Task HeadingEditPreservesRawPrefixAndTypedProvenance()
        {
            var env = new Env(); env.PutNote("A", "before\n## **見出し**\nafter");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var block = new BlockEditorWorkspace(source, "A", 1);
            Assert.That(block.Current.markdown, Is.EqualTo("見出し"));
            var result = block.ReviewDocument("A", 0, new ChangeCandidate
            { NewText = "見出しあ", SelStartBefore = 3, SelEndBefore = 3, Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "あ" } });
            Assert.That(result.Approved, Is.True);
            Assert.That(source.Current.markdown, Is.EqualTo("before\n## **見出しあ**\nafter"));
            Assert.That(block.GetLabeledDocument("A").Text, Is.EqualTo("見出しあ"));
            Assert.That(block.GetLabeledDocument("A").TypedCount(), Is.EqualTo(1));
            Assert.That(block.Current.revision, Is.EqualTo(source.Current.revision));
            await block.FlushSave();
        }
        [Test]
        public async Task RejectedPasteReturnsLocalTextAndSelectionWithoutRawSyntax()
        {
            var env = new Env(); env.PutNote("A", "# abc\nrest");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var block = new BlockEditorWorkspace(source, "A", 0);
            var result = block.ReviewDocument("A", 0, new ChangeCandidate
            { NewText = "a外部bc", SelStartBefore = 1, SelEndBefore = 1, Evidence = new InputEvidence { Kind = EvidenceKind.Paste } });
            Assert.That(result.Approved, Is.False);
            Assert.That(result.RevertText, Is.EqualTo("abc"));
            Assert.That(result.RevertSelStart, Is.EqualTo(1));
            Assert.That(result.RevertSelEnd, Is.EqualTo(1));
            Assert.That(source.Current.markdown, Is.EqualTo("# abc\nrest"));
            await block.FlushSave();
        }
        [Test]
        public async Task NewlineAtCaretPreservesOtherRawLines()
        {
            var env = new Env(); env.PutNote("A", "# ab\nend");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var block = new BlockEditorWorkspace(source, "A", 0);
            var result = block.ReviewDocument("A", 0, new ChangeCandidate
            { NewText = "a\nb", SelStartBefore = 1, SelEndBefore = 1, Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "\n" } });
            Assert.That(result.Approved, Is.True);
            Assert.That(source.Current.markdown, Is.EqualTo("# a\nb\nend"));
            await block.FlushSave();
        }
        [Test]
        public async Task ContiguousInternalCopyIntoAnotherBlockRemainsReviewed()
        {
            var env = new Env(); env.PutNote("A", "# abc\n");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var first = new BlockEditorWorkspace(source, "A", 0);
            var second = new BlockEditorWorkspace(source, "A", 1);
            first.NotifyCopied("A", 0, 3);
            var result = second.ReviewDocument("A", 0, new ChangeCandidate
            { NewText = "abc", SelStartBefore = 0, SelEndBefore = 0, Evidence = new InputEvidence { Kind = EvidenceKind.Paste } });
            Assert.That(result.Approved, Is.True);
            Assert.That(source.Current.markdown, Is.EqualTo("# abc\nabc"));
            await source.FlushSave();
        }
        [Test]
        public async Task CopyAcrossHiddenFormattingKeepsVisibleTextAndLabels()
        {
            var env = new Env(); env.PutNote("A", "**ab**cd\n");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var first = new BlockEditorWorkspace(source, "A", 0); var second = new BlockEditorWorkspace(source, "A", 1);
            first.NotifyCopied("A", 0, 4);
            var result = second.ReviewDocument("A", 0, new ChangeCandidate
            { NewText = "abcd", SelStartBefore = 0, SelEndBefore = 0, Evidence = new InputEvidence { Kind = EvidenceKind.Paste } });
            Assert.That(result.Approved, Is.True);
            Assert.That(source.Current.markdown, Is.EqualTo("**ab**cd\nabcd"));
            Assert.Throws<ArgumentException>(() => source.NotifyProjectedCopied("A", "x", new[] { 2 }));
            await source.FlushSave();
        }
        [Test]
        public async Task StaleRevisionCannotOverwriteChangesFromAnotherBlock()
        {
            var env = new Env(); env.PutNote("A", "# ab\nend");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var block = new BlockEditorWorkspace(source, "A", 0);
            source.ReviewDocument("A", 0, new ChangeCandidate { NewText = "# ab\nendx", Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "x" } });
            Assert.Throws<InvalidOperationException>(() => block.ReviewDocument("A", 0,
                new ChangeCandidate { NewText = "abc", Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "c" } }));
            await source.FlushSave();
        }
    }
}
