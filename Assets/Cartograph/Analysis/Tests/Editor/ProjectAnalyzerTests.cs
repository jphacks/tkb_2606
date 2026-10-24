using System;
using System.Linq;
using Cartograph.Core;
using NUnit.Framework;

namespace Cartograph.Analysis.Tests
{
    public class ProjectAnalyzerTests
    {
        private static AnalysisInput Note(string id, string text, long revision = 1, Origin origin = Origin.Typed)
            => new AnalysisInput(NotePath.Parse(id), new LabeledText(text, origin), revision);

        private static string E(Edge e) => e.From + " -> " + e.To;

        [Test]
        public void 仕様書の例_波動は解決され_信号処理は理由なし_存在しないノートはリンク切れ()
        {
            var r = ProjectAnalyzer.Analyze(new[]
            {
                Note("フーリエ", "## フーリエ変換\n波を見る。?どこまで\n\n==> [[波動]]: 重ね合わせ\n==> [[信号処理]]\n..> [[存在しないノート]]: 例として\n"),
                Note("波動", "# 波動\n"),
                Note("信号処理", "# 信号処理\n"),
            });

            var links = r.Edges.Where(e => !e.Auto).ToArray();
            Assert.AreEqual(2, links.Length);
            Assert.AreEqual("フーリエ#フーリエ変換 -> 波動#波動", E(links[0]));
            Assert.AreEqual(EdgeKind.Premise, links[0].Kind);
            Assert.AreEqual("重ね合わせ", links[0].Reason);
            Assert.AreEqual("フーリエ#フーリエ変換 -> 信号処理#信号処理", E(links[1]));

            Assert.AreEqual(1, r.Fogs.Count);
            var noReason = r.Marks.Where(m => m.Type == MarkType.NoReason).ToArray();
            Assert.AreEqual(1, noReason.Length);
            Assert.AreEqual("信号処理", noReason[0].Target);
            var broken = r.Marks.Where(m => m.Type == MarkType.BrokenLink).ToArray();
            Assert.AreEqual(1, broken.Length);
            Assert.AreEqual(MarkDetail.Missing, broken[0].Detail);
            Assert.AreEqual("存在しないノート", broken[0].Target);
            Assert.AreEqual(0, r.Marks.Count(m => m.Type == MarkType.Isolated));
        }

        [Test]
        public void ファイルへのリンクは最初の拠点へ_見出し付きは題名が一致する拠点へつなぐ()
        {
            var r = ProjectAnalyzer.Analyze(new[]
            {
                Note("a", "# A\n[[b]] [[b#二つ目]] [[#内部]]\n## 内部\n"),
                Note("b", "# 一つ目\n## 二つ目\n"),
            });

            var links = r.Edges.Where(e => !e.Auto).Select(E).ToArray();
            CollectionAssert.AreEqual(new[] { "a#A -> b#一つ目", "a#A -> b#一つ目/二つ目", "a#A -> a#A/内部" }, links);
        }

        [Test]
        public void 存在しない見出しへのリンクはリンク切れになる()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("a", "# A\n[[b#無い]]\n"), Note("b", "# B\n") });

            Assert.AreEqual(0, r.Edges.Count(e => !e.Auto));
            Assert.AreEqual(MarkDetail.Missing, r.Marks.Single(m => m.Type == MarkType.BrokenLink).Detail);
        }

        [Test]
        public void 同名のノートが複数あると_行き先が複数のリンク切れになり_フォルダ付きなら解決する()
        {
            var r = ProjectAnalyzer.Analyze(new[]
            {
                Note("x/メモ", "# メモX\n"),
                Note("y/メモ", "# メモY\n"),
                Note("z/参照", "# 参照\n[[メモ]]\n[[y/メモ]]\n"),
            });

            var broken = r.Marks.Single(m => m.Type == MarkType.BrokenLink);
            Assert.AreEqual(MarkDetail.Ambiguous, broken.Detail);
            Assert.AreEqual("この道の先が、複数あります", broken.Message);
            Assert.AreEqual(2, broken.Line);

            var edge = r.Edges.Single(e => !e.Auto);
            Assert.AreEqual("z/参照#参照 -> y/メモ#メモY", E(edge));
            Assert.IsTrue(edge.CrossFolder);
        }

        [Test]
        public void 同じフォルダのリンクはCrossFolderにならない()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("d/a", "# A\n[[b]]\n"), Note("d/b", "# B\n") });
            Assert.IsFalse(r.Edges.Single(e => !e.Auto).CrossFolder);
        }

        [Test]
        public void フォルダ付きで存在しないパスはリンク切れ()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("a", "# A\n[[大学/無い]] [[../x]]\n") });
            Assert.AreEqual(2, r.Marks.Count(m => m.Type == MarkType.BrokenLink && m.Detail == MarkDetail.Missing));
        }

        [Test]
        public void 孤島は_ほかのノートとつながっていないノートの最初の拠点に付く()
        {
            var r = ProjectAnalyzer.Analyze(new[]
            {
                Note("a", "# A\n## A2\n[[b]]\n"),
                Note("b", "# B\n"),
                Note("c", "# C\n## C2\n[[#C2]]\n"),   // 自分のノートの中だけ
                Note("d", "# D\n[[無い]]\n"),          // リンク切れではつながらない
            });

            var isolated = r.Nodes.Where(n => n.Isolated).Select(n => n.Id).ToArray();
            CollectionAssert.AreEqual(new[] { "c#C", "d#D" }, isolated);
            var marks = r.Marks.Where(m => m.Type == MarkType.Isolated).Select(m => m.Node).ToArray();
            CollectionAssert.AreEqual(new[] { "c#C", "d#D" }, marks);
            Assert.AreEqual("ここは、まだどこともつながっていません", r.Marks.First(m => m.Type == MarkType.Isolated).Message);
        }

        [Test]
        public void リンクの受け側も孤島ではなくなる()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("a", "# A\n[[b]]\n"), Note("b", "# B\n") });
            Assert.AreEqual(0, r.Nodes.Count(n => n.Isolated));
        }

        [Test]
        public void 自分自身の拠点へのリンクは道を作らない()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("a", "# A\n[[a]] [[#A]]\n") });
            Assert.AreEqual(0, r.Edges.Count(e => !e.Auto));
            Assert.AreEqual(0, r.Marks.Count(m => m.Type == MarkType.BrokenLink));
        }

        [Test]
        public void 見出しの親子の道は孤島の判定に数えない()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("a", "# A\n## B\n") });
            Assert.AreEqual(1, r.Edges.Count(e => e.Auto));
            Assert.IsTrue(r.Nodes[0].Isolated);
            Assert.IsFalse(r.Nodes[1].Isolated);
        }

        [Test]
        public void 履歴のないノートは_本文量が0のまま拠点と道は出る()
        {
            var r = ProjectAnalyzer.Analyze(new[]
            {
                Note("a", "# A\n貼り付けた文章 [[b]]\n", origin: Origin.Unknown),
                Note("b", "# B\nあい\n"),
            });

            Assert.AreEqual(0, r.Nodes.Single(n => n.Id == "a#A").TypedChars);
            Assert.AreEqual(2, r.Nodes.Single(n => n.Id == "b#B").TypedChars);
            Assert.AreEqual(1, r.Edges.Count(e => !e.Auto));
        }

        [Test]
        public void revisionはノートごとに結果へ引き継がれる()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("a", "# A\n", 5), Note("d/b", "# B\n", 0) });
            Assert.AreEqual(5, r.Revisions[NotePath.Parse("a")]);
            Assert.AreEqual(0, r.Revisions[NotePath.Parse("d/b")]);
            Assert.AreEqual(2, r.Revisions.Count);
        }

        [Test]
        public void ノートの追加と削除は_入力の一覧の違いとして反映される()
        {
            var a = Note("a", "# A\n[[b]]\n");
            var b = Note("b", "# B\n");

            var with = ProjectAnalyzer.Analyze(new[] { a, b });
            Assert.AreEqual(1, with.Edges.Count(e => !e.Auto));

            var without = ProjectAnalyzer.Analyze(new[] { a });
            Assert.AreEqual(0, without.Edges.Count(e => !e.Auto));
            Assert.AreEqual(1, without.Marks.Count(m => m.Type == MarkType.BrokenLink));
        }

        [Test]
        public void 空の一覧は空の結果になり_同じノートが2回渡されると例外()
        {
            var r = ProjectAnalyzer.Analyze(new AnalysisInput[0]);
            Assert.AreEqual(0, r.Nodes.Count);
            Assert.AreEqual(0, r.Revisions.Count);

            Assert.Throws<ArgumentException>(() => ProjectAnalyzer.Analyze(new[] { Note("a", ""), Note("a", "") }));
        }

        [Test]
        public void 結果の並びは入力のノートの順になる()
        {
            var r = ProjectAnalyzer.Analyze(new[] { Note("b", "# B\n"), Note("a", "# A\n") });
            CollectionAssert.AreEqual(new[] { "b#B", "a#A" }, r.Nodes.Select(n => n.Id).ToArray());
        }

        [Test]
        public void 拠点500個_道1000本の解析が現実的な時間で終わる()
        {
            var inputs = new AnalysisInput[100];
            for (int i = 0; i < inputs.Length; i++)
            {
                var sb = new System.Text.StringBuilder("# N" + i + "\n");
                for (int h = 0; h < 4; h++)
                {
                    sb.Append("## H" + h + "\n本文あいうえお？\n");
                    for (int k = 0; k < 2; k++) sb.Append("==> [[N" + ((i * 7 + h * 3 + k + 1) % inputs.Length) + "]]: 理由\n");
                    sb.Append("[[N" + ((i + h + 2) % inputs.Length) + "]] [[N" + ((i + h + 5) % inputs.Length) + "]]\n");
                }
                inputs[i] = Note("N" + i, sb.ToString());
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = ProjectAnalyzer.Analyze(inputs);
            sw.Stop();

            Assert.AreEqual(500, r.Nodes.Count);
            Assert.GreaterOrEqual(r.Edges.Count(e => !e.Auto), 1000);
            Assert.Less(sw.ElapsedMilliseconds, 1000);
        }
    }
}
