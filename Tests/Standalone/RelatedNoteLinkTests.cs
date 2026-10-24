using System;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Core;
using Cartograph.Core.Tests;
using Cartograph.EditorUI;
using Cartograph.Analysis;
using NUnit.Framework;

namespace Cartograph.IntegrationTests
{
    public class RelatedNoteLinkTests
    {
        [Test]
        public void InsertAtParagraphEndPreservesTextAndResolvesExactFolder()
        {
            string[] ids = { "A", "町/B", "他/B" };
            var edit = RelatedNoteLink.Insert("# A\n途中の文\n次の文", 6, "A", "町/B", ids);
            Assert.That(edit.Text, Is.EqualTo("# A\n途中の文\n[[町/B]]\n\n次の文"));
            var result = ProjectAnalyzer.Analyze(ids.Select(id => new AnalysisInput(NotePath.Parse(id),
                new LabeledText(id == "A" ? edit.Text : "# B", Origin.Unknown), 0)));
            Assert.That(result.Edges.Any(e => e.From == "A#A" && e.To == "町/B#B"), Is.True);
        }
        [Test]
        public void EmptyNoteCanLinkAndInvalidTargetsDoNotChangeText()
        {
            Assert.That(RelatedNoteLink.Insert("", 0, "A", "B", new[] { "A", "B" }).Text, Is.EqualTo("[[B]]\n"));
            Assert.Throws<InvalidOperationException>(() => RelatedNoteLink.Insert("文", 1, "A", "A", new[] { "A" }));
            Assert.Throws<InvalidOperationException>(() => RelatedNoteLink.Insert("文", 1, "A", "B", new[] { "A" }));
            Assert.Throws<InvalidOperationException>(() => RelatedNoteLink.Insert("文", 1, "A", "B", new[] { "A", "B", "町/B" }));
        }
        [Test]
        public async Task AddedLinkSavesAndCreatesLiveMapRoad()
        {
            var env = new Env(); env.PutNote("A", "# 始点"); env.PutNote("B", "# 相手");
            var ws = new ProjectEditorWorkspace(env.Options, env.Fs); await ws.OpenProject(env.Paths.Root);
            var edit = RelatedNoteLink.Insert(ws.Current.markdown, ws.Current.markdown.Length, "A", "B", new[] { "A", "B" });
            ws.ApplyEditorChange("A", edit.Text, ChangeOrigin.Format); await ws.FlushSave();
            var request = new Cartowords.MapPresentation.MapRequest("project", 1, ws.Documents.ToDictionary(d => d.id, d => d.revision));
            var map = await MapAnalysisAdapter.Analyze(ws, request, () => true);
            Assert.That(map.Roads.Any(e => e.From == "A#始点" && e.To == "B#相手"), Is.True);
            Assert.That(env.Body("A"), Does.Contain("[[B]]"));
        }
        [Test]
        public async Task RenameUnselectedNotePreservesSelectedNoteAndRejectsDuplicate()
        {
            var env = new Env(); env.PutNote("A", "Aの本文"); env.PutNote("B", "Bの本文");
            var ws = new ProjectEditorWorkspace(env.Options, env.Fs); await ws.OpenProject(env.Paths.Root);
            await ws.RenameDocument("B", "新しい名前");
            Assert.That(ws.Current.id, Is.EqualTo("A")); Assert.That(env.Body("新しい名前"), Is.EqualTo("Bの本文"));
            Assert.Throws<System.IO.IOException>(() => ws.RenameDocument("新しい名前", "A").GetAwaiter().GetResult());
            Assert.That(env.Body("新しい名前"), Is.EqualTo("Bの本文"));
        }
    }
}
