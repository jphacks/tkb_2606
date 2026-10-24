using System;
using System.IO;
using System.Threading.Tasks;
using Cartograph.Core;
using Cartograph.Core.Tests;
using Cartograph.EditorUI;
using NUnit.Framework;

namespace Cartograph.IntegrationTests
{
    public sealed class ProjectEditorWorkspaceTests
    {
        private static ProjectEditorWorkspace Workspace(Env env) => new ProjectEditorWorkspace(env.Options, env.Fs);
        [Test]
        public async Task ReviewedTypingPersistsAndRestoresTypedLabels()
        {
            var env = new Env(); env.PutNote("大学/授業/科目/A", "");
            var workspace = Workspace(env); await workspace.OpenProject(env.Paths.Root);
            var result = workspace.ReviewDocument(workspace.Current.id, 0, Typing.Key("あ", 0, "あ"));
            Assert.That(result.Approved, Is.True); await workspace.FlushSave();
            var again = Workspace(env); await again.OpenProject(env.Paths.Root);
            Assert.That(again.Current.markdown, Is.EqualTo("あ"));
            Assert.That(again.GetLabeledDocument(again.Current.id).TypedCount(), Is.EqualTo(1));
            Assert.That(again.Notes[0].RelativePath, Is.EqualTo("大学/授業/科目/A.md"));
        }
        [Test]
        public async Task LoadAndReviewOtherNotePreserveCurrentAndBothPersist()
        {
            var env = new Env(); env.PutNote("A", ""); env.PutNote("B", "既存");
            var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            await ws.LoadDocument("B");
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a"));
            ws.ReviewDocument("B", 0, Typing.Key("既存b", 2, "b"));
            Assert.That(ws.Current.id, Is.EqualTo("A"));
            Assert.That(ws.GetLabeledDocument("B").TypedCount(), Is.EqualTo(1));
            await ws.FlushSave();
            Assert.That(env.Body("A"), Is.EqualTo("a")); Assert.That(env.Body("B"), Is.EqualTo("既存b"));
        }
        [Test]
        public async Task ExternalPasteBlockedAndCrossNoteInternalCopyKeepsLabels()
        {
            var env = new Env(); env.PutNote("A", ""); env.PutNote("B", "");
            var ws = Workspace(env); await ws.OpenProject(env.Paths.Root); await ws.LoadDocument("B");
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a")); ws.NotifyCopied("A", 0, 1);
            var paste = new ChangeCandidate { NewText = "外部", Evidence = new InputEvidence { Kind = EvidenceKind.Paste } };
            Assert.That(ws.ReviewDocument("B", 0, paste).Approved, Is.False);
            paste.NewText = "a";
            Assert.That(ws.ReviewDocument("B", 0, paste).Approved, Is.True);
            Assert.That(ws.GetLabeledDocument("B").TypedCount(), Is.EqualTo(1));
            await ws.FlushSave(); Assert.That(env.History("B"), Does.Contain("blocked"));
        }
        [Test]
        public async Task StalePaneCannotOverwriteAndUnreviewedTypingIsForbidden()
        {
            var env = new Env(); env.PutNote("A", ""); var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a"));
            Assert.Throws<InvalidOperationException>(() => ws.ReviewDocument("A", 0, Typing.Key("b", 0, "b")));
            Assert.Throws<InvalidOperationException>(() => ws.ApplyEditorChange("A", "外部", ChangeOrigin.Typing));
            Assert.That(ws.Current.markdown, Is.EqualTo("a"));
        }
        [Test]
        public async Task SaveFailurePreventsSwitchAndRetryRecovers()
        {
            var env = new Env(); env.PutNote("A", ""); env.PutNote("B", "");
            var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a")); env.Fs.FailWrites = true;
            Assert.ThrowsAsync<IOException>(async () => await ws.OpenDocument("B"));
            Assert.That(ws.Current.id, Is.EqualTo("A")); Assert.That(ws.Current.markdown, Is.EqualTo("a"));
            env.Fs.FailWrites = false; await ws.OpenDocument("B");
            Assert.That(env.Body("A"), Is.EqualTo("a"));
        }
        [Test]
        public async Task RenameMovesBodyAndHistoryAfterConfirmedEdit()
        {
            var env = new Env(); env.PutNote("町/A", ""); var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            ws.ReviewDocument("町/A", 0, Typing.Key("a", 0, "a"));
            await ws.RenameDocument("町/A", "名前");
            Assert.That(ws.Current.id, Is.EqualTo("町/名前")); Assert.That(env.Body("町/名前"), Is.EqualTo("a"));
            Assert.That(env.History("町/名前"), Does.Contain("insert"));
        }
        [Test]
        public async Task DeleteRetainsBodyAndHistoryInTrashAndSelectsNext()
        {
            var env = new Env(); env.PutNote("A", ""); env.PutNote("B", "次");
            var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a"));
            await ws.DeleteDocument("A");
            Assert.That(ws.Current.id, Is.EqualTo("B")); Assert.That(ws.Documents.Count, Is.EqualTo(1));
            Assert.That(env.Fs.FileExists(env.Paths.NoteFile(Env.P("A"))), Is.False);
            var trash = Path.Combine(env.Paths.Root, ".cartograph", "trash");
            var folders = new System.Collections.Generic.List<string>(env.Fs.GetDirectories(trash));
            Assert.That(folders.Count, Is.EqualTo(1));
            var files = new System.Collections.Generic.List<string>(env.Fs.GetFiles(folders[0]));
            Assert.That(files.Exists(f => env.Fs.ReadAllText(f) == "a"), Is.True);
            Assert.That(files.Exists(f => (env.Fs.ReadAllText(f) ?? "").Contains("insert")), Is.True);
            await ws.DeleteDocument("B"); Assert.That(ws.Current, Is.Null); Assert.That(ws.Documents.Count, Is.EqualTo(0));
        }
        [Test]
        public async Task CloseFlushesEditsAndCanReopen()
        {
            var env = new Env(); env.PutNote("A", ""); var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a")); await ws.CloseProject();
            Assert.That(ws.Tree, Is.Null); Assert.That(ws.Current, Is.Null); Assert.That(env.Body("A"), Is.EqualTo("a"));
            Assert.That(ws.ProjectRoot, Is.Null); Assert.That(ws.Documents.Count, Is.EqualTo(0));
            await ws.OpenProject(env.Paths.Root); Assert.That(ws.Current.markdown, Is.EqualTo("a"));
            Assert.That(ws.ProjectRoot, Is.EqualTo(env.Paths.Root));
        }
        [Test]
        public async Task SwitchingProjectKeepsRootListAndCurrentInSync()
        {
            var env = new Env(); env.PutNote("A", "前の本文");
            var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            string secondRoot = Path.Combine(env.Paths.Root, "別の保存先");
            var second = ProjectWorkspace.Open(secondRoot, env.Fs, env.Options);
            await second.CreateNote(NotePath.Parse("B"));
            await ws.OpenProject(secondRoot);
            Assert.That(ws.ProjectRoot, Is.EqualTo(secondRoot)); Assert.That(ws.Current.id, Is.EqualTo("B"));
            Assert.That(ws.Documents.Count, Is.EqualTo(1)); Assert.That(ws.Documents[0].id, Is.EqualTo("B"));
            await ws.CloseProject(); await ws.CloseProject();
            Assert.That(ws.ProjectRoot, Is.Null); Assert.That(ws.Current, Is.Null); Assert.That(ws.Documents.Count, Is.EqualTo(0));
        }
        [Test]
        public async Task FailedSavePreventsCloseAndDelete()
        {
            var env = new Env(); env.PutNote("A", ""); var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a")); env.Fs.FailWrites = true;
            Assert.Throws<IOException>(() => ws.CloseProject().GetAwaiter().GetResult());
            Assert.Throws<IOException>(() => ws.DeleteDocument("A").GetAwaiter().GetResult());
            Assert.That(ws.Current.markdown, Is.EqualTo("a")); Assert.That(ws.Tree, Is.Not.Null);
            env.Fs.FailWrites = false; await ws.FlushSave();
        }
        [Test]
        public async Task NewlineAtMiddleRetainsTypingLabels()
        {
            var env = new Env(); env.PutNote("A", "ab"); var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            var change = Typing.Key("a\nb", 1, "\n");
            Assert.That(ws.ReviewDocument("A", 0, change).Approved, Is.True);
            await ws.FlushSave(); Assert.That(env.Body("A"), Is.EqualTo("a\nb"));
            Assert.That(ws.GetLabeledDocument("A").Slice(1, 1).Runs[0].Origin, Is.EqualTo(Origin.Typed));
        }
        [Test]
        public async Task EmptyProjectDoesNotInventSavedContent()
        {
            var env = new Env(); var ws = Workspace(env); await ws.OpenProject(env.Paths.Root);
            Assert.That(ws.Current, Is.Null); await ws.CreateDocument();
            Assert.That(ws.Current.title, Is.EqualTo("無題のノート")); Assert.That(env.Body("無題のノート"), Is.EqualTo(""));
        }
    }
}
