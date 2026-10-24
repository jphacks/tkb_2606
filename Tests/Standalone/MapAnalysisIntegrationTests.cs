using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cartograph.Core;
using Cartograph.Core.Tests;
using Cartograph.EditorUI;
using Cartograph.EditorUI.Navigation;
using Cartograph.Analysis;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.IntegrationTests
{
    public sealed class MapAnalysisIntegrationTests
    {
        [TestCase("# 見出し\n**太字**と*斜体*", "見出し太字と斜体")]
        [TestCase("- [x] 完了\n1. 次\n> 引用", "完了次引用")]
        [TestCase("[表示](https://example.com) [[別ノート|ラベル]]", "表示 ラベル")]
        [TestCase("```cs\na * b\n```", "a * b")]
        [TestCase("`**記号**`と~~取り消し~~", "**記号**と取り消し")]
        [TestCase("---\nタイトル\n===", "タイトル")]
        [TestCase("普通の # と a_b と 2 * 3", "普通の # と a_b と 2 * 3")]
        [TestCase("## 😀あ\r\n", "😀あ")]
        [TestCase("\\*星\\*", "*星*")]
        public void VisibleTextExcludesFormatting(string markdown, string expected)
        {
            Assert.That(MarkdownTextCount.PlainText(markdown), Is.EqualTo(expected));
            Assert.That(MarkdownTextCount.Count(markdown), Is.EqualTo(new System.Globalization.StringInfo(expected).LengthInTextElements));
        }
        [Test]
        public async Task LoadsAllNotesAndConvertsLinksFlowersAndLines()
        {
            var env = new Env(); env.PutNote("A", "# 学び\n## 次\n[[B]]\n嬉しい"); env.PutNote("B", "# 相手\nTODO: 調べる");
            var ws = new ProjectEditorWorkspace(env.Options, env.Fs); await ws.OpenProject(env.Paths.Root);
            foreach (char character in "嬉しい")
            {
                var record = ws.Current;
                Assert.That(ws.ReviewDocument(record.id, record.revision,
                    Typing.Key(record.markdown + character, record.markdown.Length, character.ToString())).Approved, Is.True);
            }
            var request = new MapRequest("p", 1, ws.Documents.ToDictionary(d => d.id, d => d.revision));
            var result = await MapAnalysisAdapter.Analyze(ws, request, () => true);
            Assert.That(result.Nodes.Count, Is.EqualTo(3)); Assert.That(result.Nodes.Single(n => n.Title == "次").Line, Is.EqualTo(2));
            Assert.That(result.Roads.Count, Is.GreaterThanOrEqualTo(2)); Assert.That(result.Flowers.Count, Is.GreaterThan(0));
            Assert.That(result.Fogs.Count, Is.EqualTo(1)); Assert.That(ws.Current.id, Is.EqualTo("A"));
            Assert.That(ws.GetLabeledDocument("B").Text, Does.Contain("相手"));
        }
        [Test]
        public async Task RejectsStaleInputsAndOldProjectScope()
        {
            var env = new Env(); env.PutNote("A", ""); var ws = new ProjectEditorWorkspace(env.Options, env.Fs);
            await ws.OpenProject(env.Paths.Root);
            var request = new MapRequest("p", 1, ws.Documents.ToDictionary(d => d.id, d => d.revision));
            ws.ReviewDocument("A", 0, Typing.Key("a", 0, "a"));
            Assert.Throws<OperationCanceledException>(() => MapAnalysisAdapter.Analyze(ws, request, () => true).GetAwaiter().GetResult());
            var fresh = new MapRequest("p", 2, ws.Documents.ToDictionary(d => d.id, d => d.revision));
            Assert.Throws<OperationCanceledException>(() => MapAnalysisAdapter.Analyze(ws, fresh, () => false).GetAwaiter().GetResult());
        }
        [Test]
        public void RejectsMismatchedAnalysisRevisions()
        {
            var request = new MapRequest("p", 1, new Dictionary<string, long> { { "A", 3 } });
            var analysis = ProjectAnalyzer.Analyze(new[] { new AnalysisInput(NotePath.Parse("A"), new LabeledText("# A", Origin.Unknown), 2) });
            Assert.Throws<InvalidOperationException>(() => MapAnalysisAdapter.Convert(request, analysis));
        }
        [Test]
        public void AddingHeadingPinsExistingPositionsAndRemovingItClearsItsPosition()
        {
            var request = new MapRequest("p", 1, new Dictionary<string, long>());
            var a = new MapNode("A#a", "A", "a", 1, 40);
            var first = MapLayout.Solve(new MapSnapshot(request, new[] { a }));
            var b = new MapNode("A#b", "A", "b", 2, 40);
            var second = MapLayout.Solve(new MapSnapshot(request, new[] { a, b }), first);
            Assert.That(second[a.Id].X, Is.EqualTo(first[a.Id].X)); Assert.That(second[a.Id].Y, Is.EqualTo(first[a.Id].Y));
            Assert.That(second[b.Id].X != second[a.Id].X || second[b.Id].Y != second[a.Id].Y, Is.True);
            Assert.That(MapLayout.Solve(new MapSnapshot(request, new[] { a }), second).ContainsKey(b.Id), Is.False);
        }
    }
}
