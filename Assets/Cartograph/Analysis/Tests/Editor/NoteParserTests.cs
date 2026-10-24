using System.Linq;
using Cartograph.Core;
using NUnit.Framework;

namespace Cartograph.Analysis.Tests
{
    public class NoteParserTests
    {
        private static NoteAnalysis Parse(string text, Origin origin = Origin.Typed, string note = "科目/ノート", long revision = 1)
            => NoteParser.Parse(new AnalysisInput(NotePath.Parse(note), new LabeledText(text, origin), revision));

        private const string Sample =
            "## フーリエ変換\n" +
            "波を周波数に分けて見る方法。?どこまで厳密に書くか\n" +
            "\n" +
            "==> [[波動]]: 重ね合わせで説明できるから\n" +
            "==> [[信号処理]]\n" +
            "..> [[存在しないノート]]: 例として\n";

        [Test]
        public void 仕様書の例で_拠点と霧と道と理由なしの目印が出る()
        {
            var r = Parse(Sample, revision: 7);

            Assert.AreEqual(7, r.Revision);
            Assert.AreEqual(1, r.Nodes.Count);
            Assert.AreEqual("科目/ノート#フーリエ変換", r.Nodes[0].Id);
            Assert.AreEqual(2, r.Nodes[0].Level);
            Assert.AreEqual(1, r.Nodes[0].Line);

            Assert.AreEqual(1, r.Fogs.Count);
            Assert.AreEqual(2, r.Fogs[0].Line);
            Assert.AreEqual(r.Nodes[0].Id, r.Fogs[0].Node);

            Assert.AreEqual(3, r.Links.Count);
            Assert.AreEqual(EdgeKind.Premise, r.Links[0].Kind);
            Assert.AreEqual("波動", r.Links[0].TargetNote);
            Assert.AreEqual("重ね合わせで説明できるから", r.Links[0].Reason);
            Assert.AreEqual(4, r.Links[0].Line);
            Assert.AreEqual(EdgeKind.Premise, r.Links[1].Kind);
            Assert.IsNull(r.Links[1].Reason);
            Assert.AreEqual(EdgeKind.Example, r.Links[2].Kind);
            Assert.AreEqual("例として", r.Links[2].Reason);

            Assert.AreEqual(1, r.Marks.Count);
            Assert.AreEqual(MarkType.NoReason, r.Marks[0].Type);
            Assert.AreEqual(5, r.Marks[0].Line);
            Assert.AreEqual("信号処理", r.Marks[0].Target);
        }

        [Test]
        public void 見出しの木から親子の道を作り_最上位の見出しは最初の見出しにつなぐ()
        {
            var r = Parse("# A\n## B\n### C\n## D\n# E\n");

            Assert.AreEqual(new[] { "科目/ノート#A", "科目/ノート#A/B", "科目/ノート#A/B/C", "科目/ノート#A/D", "科目/ノート#E" },
                r.Nodes.Select(n => n.Id).ToArray());
            var edges = r.AutoEdges.Select(e => e.From.Substring(e.From.IndexOf('#') + 1) + ">" + e.To.Substring(e.To.IndexOf('#') + 1)).ToArray();
            CollectionAssert.AreEquivalent(new[] { "A>A/B", "A/B>A/B/C", "A>A/D", "A>E" }, edges);
            Assert.IsTrue(r.AutoEdges.All(e => e.Auto && !e.CrossFolder && e.Kind == EdgeKind.Unspecified));
        }

        [Test]
        public void 深さを飛ばした見出しは_直近の上位の見出しの子になる()
        {
            var r = Parse("# A\n### C\n");
            Assert.AreEqual("科目/ノート#A/C", r.Nodes[1].Id);
            Assert.AreEqual(1, r.AutoEdges.Count);
        }

        [Test]
        public void 同じ階層の同名の見出しには連番が付く()
        {
            var r = Parse("# A\n## 例\n## 例\n## 例\n");
            Assert.AreEqual(new[] { "科目/ノート#A", "科目/ノート#A/例", "科目/ノート#A/例~2", "科目/ノート#A/例~3" },
                r.Nodes.Select(n => n.Id).ToArray());
        }

        [Test]
        public void 見出しのないファイルは_ファイル名の1拠点になる()
        {
            var r = Parse("ただの文章。\n[[他]]\n");

            Assert.AreEqual(1, r.Nodes.Count);
            Assert.AreEqual("科目/ノート#", r.Nodes[0].Id);
            Assert.AreEqual("ノート", r.Nodes[0].Title);
            Assert.AreEqual(0, r.Nodes[0].Level);
            Assert.AreEqual(1, r.Nodes[0].Line);
            Assert.AreEqual(0, r.AutoEdges.Count);
            Assert.AreEqual(r.Nodes[0].Id, r.Links[0].FromNode);
        }

        [Test]
        public void 空のファイルでも落ちず_1拠点になる()
        {
            var r = Parse("");
            Assert.AreEqual(1, r.Nodes.Count);
            Assert.AreEqual(0, r.Nodes[0].TypedChars);
        }

        [Test]
        public void 見出しより前のリンクと霧は_最初の見出しの所属になる()
        {
            var r = Parse("前置き？ [[X]]\n# A\n## B\n[[Y]]\n");

            Assert.AreEqual("科目/ノート#A", r.Links[0].FromNode);
            Assert.AreEqual("科目/ノート#A", r.Fogs[0].Node);
            Assert.AreEqual("科目/ノート#A/B", r.Links[1].FromNode);
            Assert.AreEqual(1, r.Fogs[0].Line);
        }

        [Test]
        public void コードブロックの中は解析しない()
        {
            var r = Parse("# A\n```\n# 見出しではない\n[[リンク]]\n?TODO:\n```\n[[外]]\n");

            Assert.AreEqual(1, r.Nodes.Count);
            Assert.AreEqual(1, r.Links.Count);
            Assert.AreEqual("外", r.Links[0].TargetNote);
            Assert.AreEqual(0, r.Fogs.Count);
            Assert.AreEqual("[[外]]".Length, r.Nodes[0].TypedChars);   // コードブロックの中は数えない
        }

        [Test]
        public void 閉じていないコードブロックは_最後まで解析しない()
        {
            var r = Parse("# A\n```\n[[リンク]]\n");
            Assert.AreEqual(0, r.Links.Count);
        }

        [Test]
        public void 霧の判定_全角と_TODOと_URLの中は除く()
        {
            var r = Parse("# A\nなぜ？\nTODO: あとで\nTODO：あとで\nhttps://example.com/a?b=1\n見て(http://x.y/?q) 次は?\n普通の文\n");

            Assert.AreEqual(new[] { 2, 3, 4, 6 }, r.Fogs.Select(f => f.Line).ToArray());
        }

        [Test]
        public void 見出し行の霧は_その見出しの所属になる()
        {
            var r = Parse("# A\n## なぜ？\n");
            Assert.AreEqual("科目/ノート#A/なぜ？", r.Fogs[0].Node);
        }

        [Test]
        public void リンクの書き方_フォルダ付きと見出し付きと同じファイル()
        {
            var r = Parse("# A\n[[大学/授業/ノート]] [[波動#基礎]] [[#B]] [[x#]]\n");

            Assert.AreEqual(4, r.Links.Count);
            Assert.AreEqual("大学/授業/ノート", r.Links[0].TargetNote);
            Assert.IsNull(r.Links[0].TargetHeading);
            Assert.AreEqual("波動", r.Links[1].TargetNote);
            Assert.AreEqual("基礎", r.Links[1].TargetHeading);
            Assert.AreEqual("", r.Links[2].TargetNote);
            Assert.AreEqual("B", r.Links[2].TargetHeading);
            Assert.AreEqual("x", r.Links[3].TargetNote);
            Assert.IsNull(r.Links[3].TargetHeading);
            Assert.IsTrue(r.Links.All(l => l.Kind == EdgeKind.Unspecified && l.Reason == null));
            Assert.AreEqual(0, r.Marks.Count);
        }

        [Test]
        public void 種別と理由_全角コロンと対比と前後の空白()
        {
            var r = Parse("# A\n  <-> [[B]] ： 立場が逆だから \n..> [[C]]:\n");

            Assert.AreEqual(EdgeKind.Contrast, r.Links[0].Kind);
            Assert.AreEqual("立場が逆だから", r.Links[0].Reason);
            Assert.AreEqual(EdgeKind.Example, r.Links[1].Kind);
            Assert.IsNull(r.Links[1].Reason);
            Assert.AreEqual(1, r.Marks.Count);
            Assert.AreEqual(3, r.Marks[0].Line);
        }

        [Test]
        public void 種別のある行の2つ目のリンクは_ふつうのリンクになる()
        {
            var r = Parse("# A\n==> [[B]]: 理由 [[C]]\n");

            Assert.AreEqual(EdgeKind.Premise, r.Links[0].Kind);
            Assert.AreEqual("理由 [[C]]", r.Links[0].Reason);
            Assert.AreEqual(2, r.Links.Count);
            Assert.AreEqual(EdgeKind.Unspecified, r.Links[1].Kind);

            var r2 = Parse("# A\n==> [[B]] [[C]]: 理由\n");
            Assert.AreEqual(2, r2.Links.Count);
            Assert.AreEqual(EdgeKind.Premise, r2.Links[0].Kind);
            Assert.IsNull(r2.Links[0].Reason);
            Assert.AreEqual(EdgeKind.Unspecified, r2.Links[1].Kind);
        }

        [Test]
        public void 本文量は_見出し直下の_typedの文字だけを空白抜きで数える()
        {
            var doc = new LabeledText("", Origin.Unknown);
            string s = "# A\nあ い\nうえ\n## B\nお\n";
            doc.Insert(0, s, LabeledSlice.Uniform(s.Length, Origin.Typed));
            // "うえ" を貼り付けた文字(unknown)に置き換える
            int at = s.IndexOf("うえ");
            doc.Delete(at, 2);
            doc.Insert(at, "うえ", LabeledSlice.Uniform(2, Origin.Unknown));

            var r = NoteParser.Parse(new AnalysisInput(NotePath.Parse("n"), doc, 1));
            Assert.AreEqual(2, r.Nodes[0].TypedChars);   // "あい" だけ(見出し行と空白は数えない)
            Assert.AreEqual(1, r.Nodes[1].TypedChars);   // "お"
        }

        [Test]
        public void 履歴のない文章は_本文量が0になる()
        {
            var r = Parse("# A\nあいう\n", Origin.Unknown);
            Assert.AreEqual(0, r.Nodes[0].TypedChars);
        }

        [Test]
        public void 行番号は1始まりで_CRLFとCRも1つの改行として数える()
        {
            var r = Parse("# A\r\n本文\r\n## B\r\n?\r\n");

            Assert.AreEqual(3, r.Nodes[1].Line);
            Assert.AreEqual(4, r.Fogs[0].Line);
            Assert.AreEqual("?", r.Fogs[0].Text);

            var r2 = Parse("# A\r## B\r?");
            Assert.AreEqual(2, r2.Nodes[1].Line);
            Assert.AreEqual(3, r2.Fogs[0].Line);
        }

        [Test]
        public void 見出しの位置はUTF16で_サロゲートペアでもずれない()
        {
            string text = "# A\n😀😀\n## B\n";
            var r = Parse(text);
            Assert.AreEqual(text.IndexOf("## B"), r.Nodes[1].Position);
            Assert.AreEqual(0, r.Nodes[0].Position);
        }

        [Test]
        public void 見出しとして扱わない書き方()
        {
            var r = Parse("#タグ\n#### 四つ\n# \n本文\n");
            Assert.AreEqual(1, r.Nodes.Count);
            Assert.AreEqual(0, r.Nodes[0].Level);
        }

        [Test]
        public void 閉じの記号つき見出しは_題名から記号を除く()
        {
            var r = Parse("## 題名 ##\n");
            Assert.AreEqual("題名", r.Nodes[0].Title);
        }
    }
}
