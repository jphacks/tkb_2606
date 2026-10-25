using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Core;
using NUnit.Framework;

namespace Cartograph.Analysis.Tests
{
    /// <summary>
    /// 「表示サンプル」のデモプロジェクト(メモリ上)を、本物と同じ道(ProjectWorkspace → NoteSession → ProjectAnalyzer)で解析して、
    /// 説明している機能がすべて地図に出ることを確かめる。
    /// </summary>
    public class DemoProjectTests
    {
        private sealed class Opened
        {
            public InMemoryFileSystem Fs;
            public ProjectWorkspace Workspace;
            public Dictionary<string, NoteSession> Sessions = new Dictionary<string, NoteSession>();

            public AnalysisResult Analyze()
                => ProjectAnalyzer.Analyze(Sessions.Select(p => new AnalysisInput(NotePath.Parse(p.Key), p.Value.CloneDocument(), 1)), EmotionLexicon.Default);
        }

        private static async Task<Opened> OpenDemo()
        {
            var fs = DemoProject.CreateFileSystem();
            var ws = ProjectWorkspace.Open(DemoProject.Root, fs, new NoteSessionOptions { SaveDebounceMs = -1 });
            var opened = new Opened { Fs = fs, Workspace = ws };
            foreach (var note in ws.AllNotes()) opened.Sessions[note.Value] = await ws.OpenNote(note);
            return opened;
        }

        [Test]
        public async Task 通常のプロジェクトと同じ道で開け_3つのフォルダに十数個のノートがある()
        {
            var o = await OpenDemo();
            Assert.That(o.Sessions.Count, Is.InRange(12, 14));
            Assert.AreEqual(DemoProject.Notes.Count, o.Sessions.Count);
            Assert.That(o.Workspace.Tree.Folders.Count, Is.EqualTo(3));
            Assert.IsTrue(o.Workspace.NoteExists(NotePath.Parse(DemoProject.FirstNote)));
            foreach (var note in DemoProject.Notes) Assert.AreEqual(note.Text, o.Sessions[note.Path].Text, note.Path);
        }

        [Test]
        public async Task 見出しは1から3までのレベルがそろう()
        {
            var r = (await OpenDemo()).Analyze();
            for (int level = 1; level <= 3; level++) Assert.IsTrue(r.Nodes.Any(n => n.Level == level), "level " + level);
        }

        [Test]
        public async Task 疑問とTODOの行は霧になる()
        {
            var r = (await OpenDemo()).Analyze();
            Assert.That(r.Fogs.Count, Is.GreaterThanOrEqualTo(3));
            Assert.IsTrue(r.Fogs.Any(f => f.Text.StartsWith("TODO:")));
            Assert.IsTrue(r.Fogs.Any(f => f.Text.Contains("？")));
        }

        [Test]
        public async Task Four種類の感情の花と枯れた花が咲く()
        {
            var r = (await OpenDemo()).Analyze();
            foreach (Emotion e in new[] { Emotion.Joy, Emotion.Anger, Emotion.Sorrow, Emotion.Pleasure })
                Assert.IsTrue(r.Flowers.Any(f => f.Emotion == e && !f.Withered), "flower " + e);
            Assert.IsTrue(r.Flowers.Any(f => f.Withered));
        }

        [Test]
        public async Task 貼りつけた文章には花が咲かず_土地も育たない()
        {
            var r = (await OpenDemo()).Analyze();
            var pasted = r.Nodes.Where(n => n.File.Value == "地図のしくみ/貼りつけた文章").ToList();
            Assert.IsNotEmpty(pasted);
            Assert.IsTrue(pasted.All(n => n.TypedChars == 0));
            Assert.IsFalse(r.Flowers.Any(f => pasted.Any(n => n.Id == f.Node)));
        }

        [Test]
        public async Task 道は3種類と種類なしがそろい_フォルダをまたぐ道もある()
        {
            var r = (await OpenDemo()).Analyze();
            var links = r.Edges.Where(e => !e.Auto).ToList();
            foreach (EdgeKind kind in new[] { EdgeKind.Premise, EdgeKind.Contrast, EdgeKind.Example, EdgeKind.Unspecified })
                Assert.IsTrue(links.Any(e => e.Kind == kind), "edge " + kind);
            Assert.IsTrue(links.Any(e => e.CrossFolder));
            Assert.IsTrue(r.Edges.Any(e => e.Auto), "見出しの親子の道");
            Assert.IsTrue(links.Any(e => e.Reason != null));
        }

        [Test]
        public async Task 理由のない道_切れたリンク_孤島の印が付く()
        {
            var r = (await OpenDemo()).Analyze();
            Assert.IsTrue(r.Marks.Any(m => m.Type == MarkType.NoReason && m.Target == "霧と疑問"));
            Assert.IsTrue(r.Marks.Any(m => m.Type == MarkType.BrokenLink && m.Detail == MarkDetail.Missing && m.Target == "存在しない"));
            var isolated = r.Nodes.Where(n => n.Isolated).Select(n => n.File.Value).Distinct().ToList();
            CollectionAssert.AreEqual(new[] { "地図のしくみ/ひとりぼっちの島" }, isolated);
        }

        [Test]
        public async Task 固有名詞ノートの別名で建物が建ち_言及の多い場所は大きい()
        {
            var r = (await OpenDemo()).Analyze();
            Assert.IsTrue(r.Buildings.Any(b => b.ProperNounNote.Value == "人と場所/田中"));
            Assert.IsTrue(r.Buildings.Any(b => b.ProperNounNote.Value == "人と場所/図書館"));
            int Score(Building b) => b.Strong * 3 + b.Medium * 2 + b.Weak;
            Assert.That(r.Buildings.Max(Score), Is.GreaterThanOrEqualTo(14), "一番大きい建物は、ビルになる点数");
            Assert.That(r.Buildings.Min(Score), Is.LessThan(2), "一番小さい建物は、小屋になる点数");
        }

        [Test]
        public async Task 文字数で自然の段階が変わる()
        {
            var r = (await OpenDemo()).Analyze();
            Assert.IsTrue(r.Nodes.Any(n => n.TypedChars >= 900), "森");
            Assert.IsTrue(r.Nodes.Any(n => n.TypedChars >= 400 && n.TypedChars < 900), "木");
            Assert.IsTrue(r.Nodes.Any(n => n.TypedChars >= 200 && n.TypedChars < 400), "茂み");
            Assert.IsTrue(r.Nodes.Any(n => n.TypedChars > 0 && n.TypedChars < 200), "小さな土地");
        }

        [Test]
        public async Task 履歴で打った文字として読み込まれ_貼りつけは打った文字に数えない()
        {
            var o = await OpenDemo();
            Assert.That(o.Sessions["はじめに/ようこそ"].TypedCount(), Is.GreaterThan(0));
            Assert.AreEqual(0, o.Sessions["地図のしくみ/貼りつけた文章"].TypedCount());
            Assert.AreEqual(HistoryStatus.Clean, o.Sessions["はじめに/ようこそ"].HistoryStatus);
        }

        [Test]
        public async Task 文章を書き換えると解析結果が変わる()
        {
            var o = await OpenDemo();
            int before = o.Analyze().Fogs.Count;
            var session = o.Sessions["はじめに/保存されません"];
            int pos = session.Text.IndexOf('\n');
            var result = session.Apply(new ChangeCandidate
            {
                NewText = session.Text.Insert(pos, "？"),
                SelStartBefore = pos,
                SelEndBefore = pos,
                Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "？" },
            });
            Assert.IsTrue(result.Approved);
            Assert.AreEqual(before + 1, o.Analyze().Fogs.Count);
        }

        [Test]
        public async Task 編集してもデモのファイルはメモリの中にだけ書かれる()
        {
            var o = await OpenDemo();
            var session = o.Sessions["はじめに/保存されません"];
            int pos = session.Text.Length;
            session.Apply(new ChangeCandidate
            {
                NewText = session.Text + "あ",
                SelStartBefore = pos,
                SelEndBefore = pos,
                Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "あ" },
            });
            await session.Flush();
            Assert.IsTrue(o.Fs.ReadAllText(new ProjectPaths(DemoProject.Root).NoteFile(NotePath.Parse("はじめに/保存されません"))).EndsWith("あ"));
            Assert.IsFalse(System.IO.Directory.Exists(DemoProject.Root), "仮想ルートはディスク上に作られない");
        }

        [Test]
        public void ノート名は一意で_リンクの行き先に使える()
        {
            var names = DemoProject.Notes.Select(n => NotePath.Parse(n.Path).Name).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count());
        }
    }
}
