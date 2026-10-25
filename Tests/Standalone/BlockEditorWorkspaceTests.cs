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
        public async Task FolderNavigationOpensOnlyDirectNotesAndEmptyFolderClearsCurrent()
        {
            var env = new Env(); env.PutNote("a/親のノート", "本文"); env.PutNote("a/b/子のノート", "本文");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            source.CreateFolder("", "空");
            await source.OpenFolder("a"); Assert.That(source.Current.id, Is.EqualTo("a/親のノート"));
            await source.OpenFolder("a/b"); Assert.That(source.Current.id, Is.EqualTo("a/b/子のノート"));
            await source.OpenFolder("空"); Assert.That(source.Current, Is.Null);
            await source.CreateDocumentInFolder("空"); Assert.That(source.Current.id, Is.EqualTo("空/無題のノート"));
            await source.FlushSave();
        }
        [Test]
        public async Task RenamingChapterUpdatesIncomingLinksAndPreservesHeadingAndCode()
        {
            var env = new Env();
            env.PutNote("A", "# A\n[[B]]\n[[B#見出し]]\n```\n[[B]]\n```\n");
            env.PutNote("B", "# 見出し\n[[A]]\n");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            await source.RenameDocument("B", "新しい名前");
            var a = await source.LoadDocument("A");
            Assert.That(a.markdown, Is.EqualTo("# A\n[[新しい名前]]\n[[新しい名前#見出し]]\n```\n[[B]]\n```\n"));
            var reopened = new ProjectEditorWorkspace(env.Options, env.Fs); await reopened.OpenProject(env.Paths.Root);
            Assert.That((await reopened.LoadDocument("A")).markdown, Is.EqualTo(a.markdown));
            Assert.That((await reopened.LoadDocument("新しい名前")).markdown, Is.EqualTo("# 見出し\n[[A]]\n"));
            await reopened.FlushSave();
        }
        [Test]
        public async Task NestedFoldersAndNotesSurviveReopening()
        {
            var env = new Env();
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            source.CreateFolder("", "大学"); source.CreateFolder("大学", "授業");
            await source.CreateDocumentInFolder("大学/授業");
            Assert.That(source.Current.id, Is.EqualTo("大学/授業/無題のノート"));
            var reopened = new ProjectEditorWorkspace(env.Options, env.Fs); await reopened.OpenProject(env.Paths.Root);
            Assert.That(reopened.Tree.Folders[0].Folders[0].Name, Is.EqualTo("授業"));
            Assert.Throws<System.IO.IOException>(() => reopened.CreateFolder("大学", "授業"));
            Assert.Throws<ArgumentException>(() => reopened.CreateFolder("", "../外"));
            await source.FlushSave(); await reopened.FlushSave();
        }
        [Test]
        public async Task UndoUsesEditDiffInsteadOfTheLaterSelectAllRange()
        {
            var env = new Env(); env.PutNote("A", "abc\ndef");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var original = source.GetLabeledDocument("A");
            Assert.That(source.ReviewDocument("A", source.Current.revision, new ChangeCandidate
            { NewText = "abc\ndefX", SelStartBefore = 7, SelEndBefore = 7,
                Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "X" } }).Approved, Is.True);
            var diff = TextDiff.Compute(source.Current.markdown, original.Text);
            var undo = source.ReviewDocument("A", source.Current.revision, new ChangeCandidate
            {
                NewText = original.Text, SelStartBefore = diff.Pos, SelEndBefore = diff.Pos + diff.Deleted.Length,
                Evidence = new InputEvidence { Kind = EvidenceKind.Undo, Text = diff.Inserted,
                    UndoLabels = original.Slice(diff.Pos, diff.Inserted.Length) }
            });
            Assert.That(undo.Approved, Is.True);
            Assert.That(source.Current.markdown, Is.EqualTo("abc\ndef"));
            await source.FlushSave();
        }
        [Test]
        public async Task ImeReplacementSpansMultipleRowsWithoutChangingOtherText()
        {
            var env = new Env(); env.PutNote("A", "abc\ndef\nghi");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var block = new BlockEditorWorkspace(source, "A", 0);
            block.ReplacementSelection = () => Tuple.Create(1, 6);
            var result = block.ReviewDocument("A", source.Current.revision, new ChangeCandidate
            {
                NewText = "日本語abc", SelStartBefore = 0, SelEndBefore = 0,
                Evidence = new InputEvidence { Kind = EvidenceKind.ImeCommit, Text = "日本語" }
            });
            Assert.That(result.Approved, Is.True);
            Assert.That(source.Current.markdown, Is.EqualTo("a日本語f\nghi"));
            await source.FlushSave();
        }
        [Test]
        public async Task MultiRowReplacementStillRejectsExternalPaste()
        {
            var env = new Env(); env.PutNote("A", "abc\ndef");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs); await source.OpenProject(env.Paths.Root);
            var block = new BlockEditorWorkspace(source, "A", 0);
            block.ReplacementSelection = () => Tuple.Create(0, 7);
            var result = block.ReviewDocument("A", source.Current.revision, new ChangeCandidate
            { NewText = "外部abc", Evidence = new InputEvidence { Kind = EvidenceKind.Paste } });
            Assert.That(result.Approved, Is.False);
            Assert.That(source.Current.markdown, Is.EqualTo("abc\ndef"));
            await source.FlushSave();
        }
        [Test]
        public async Task BookTitlePersistsIndependentlyOfChapterNames()
        {
            var env = new Env(); env.PutNote("第一章", "本文");
            var source = new ProjectEditorWorkspace(env.Options, env.Fs);
            await source.OpenProject(env.Paths.Root);
            source.UpdateBookTitle("わたしの本");
            Assert.That(source.Current.title, Is.EqualTo("第一章"));
            await source.RenameDocument("第一章", "はじまり");
            Assert.That(source.BookTitle, Is.EqualTo("わたしの本"));
            var reopened = new ProjectEditorWorkspace(env.Options, env.Fs);
            await reopened.OpenProject(env.Paths.Root);
            Assert.That(reopened.BookTitle, Is.EqualTo("わたしの本"));
            Assert.That(reopened.Current.title, Is.EqualTo("はじまり"));
            await source.FlushSave(); await reopened.FlushSave();
        }
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
