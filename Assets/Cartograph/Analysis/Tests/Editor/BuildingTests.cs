using System.Linq;
using Cartograph.Core;
using NUnit.Framework;

namespace Cartograph.Analysis.Tests
{
    public class BuildingTests
    {
        private static AnalysisInput Note(string id, string text, Origin origin = Origin.Typed)
            => new AnalysisInput(NotePath.Parse(id), new LabeledText(text, origin), 1);

        private static Building[] Buildings(params AnalysisInput[] inputs)
            => ProjectAnalyzer.Analyze(inputs).Buildings.ToArray();

        private static readonly AnalysisInput Tanaka = Note("人物/田中", "別名: 田中, たなか\n# 田中さん\n");

        [Test]
        public void 別名の書式は読点_カンマ_全角コロンを受け付け_空と重複は捨てる()
        {
            CollectionAssert.AreEqual(new[] { "田中", "たなか", "Tanaka" },
                ProperNounScanner.ParseAliases("別名： 田中、たなか,，Tanaka, 田中 ,"));
            Assert.AreEqual(0, ProperNounScanner.ParseAliases("別名:").Count);
            Assert.AreEqual(0, ProperNounScanner.ParseAliases("別名: ,、 ,").Count);
            Assert.AreEqual(0, ProperNounScanner.ParseAliases("メモ: 田中").Count);
        }

        [Test]
        public void 先頭行が別名のノートだけが固有名詞ノート()
        {
            var plain = NoteParser.Parse(Note("a", "# A\n別名: 田中\n"));
            Assert.AreEqual(0, plain.Aliases.Count);
            var proper = NoteParser.Parse(Note("a", "別名: 田中\n# A\n"));
            CollectionAssert.AreEqual(new[] { "田中" }, proper.Aliases);
            Assert.AreEqual(0, proper.ScanLines.Count);
        }

        [Test]
        public void 別名が出た拠点ごとに建物が建ち_出現回数を数える()
        {
            var b = Buildings(Tanaka, Note("日記", "# 月曜\n田中と会った。たなかは笑った。\n# 火曜\n田中の話。\n# 水曜\n何もなし。\n"));
            Assert.AreEqual(2, b.Length);
            Assert.AreEqual("日記#月曜", b[0].Node);
            Assert.AreEqual(2, b[0].Count);
            Assert.AreEqual("日記#火曜", b[1].Node);
            Assert.AreEqual(1, b[1].Count);
            Assert.AreEqual(NotePath.Parse("人物/田中"), b[0].ProperNounNote);
        }

        [Test]
        public void 強調は見出し_太字_通常本文の順に強い()
        {
            var b = Buildings(Tanaka, Note("日記", "# 田中の日\n**田中**が来た。田中も来た。__田中__。\n")).Single();
            Assert.AreEqual(1, b.Strong);
            Assert.AreEqual(2, b.Medium);
            Assert.AreEqual(1, b.Weak);
        }

        [Test]
        public void リンクの表示名は数え_固有名詞ノート自身は数えない()
        {
            var b = Buildings(Tanaka, Note("日記", "# 日\n[[田中]]を読む。\n"));
            Assert.AreEqual(1, b.Single().Weak);
            Assert.AreEqual(0, Buildings(Note("人物/田中", "別名: 田中\n# 田中\n田中は田中だ。\n")).Length);
        }

        [Test]
        public void コードブロックとインラインコードは数えない()
        {
            Assert.AreEqual(0, Buildings(Tanaka, Note("日記", "# 日\n```\n田中\n```\n`田中`\n")).Length);
        }

        [Test]
        public void 全角半角と大文字小文字の揺れは同じ別名として扱う()
        {
            var b = Buildings(Note("人物/ABC", "別名: ＡＢＣ商会\n"), Note("日記", "# 日\nabc商会へ行った。ＡＢＣ商会も。\n"));
            Assert.AreEqual(2, b.Single().Count);
        }

        [Test]
        public void 最長一致で数える()
        {
            var b = Buildings(Note("a", "別名: 田中\n"), Note("b", "別名: 田中太郎\n"), Note("日記", "# 日\n田中太郎と田中。\n"));
            Assert.AreEqual(2, b.Length);
            Assert.AreEqual("b", b[0].ProperNounNote.Value);
            Assert.AreEqual("a", b[1].ProperNounNote.Value);
        }

        [Test]
        public void 一文字の別名は前後に漢字があれば検出しない()
        {
            var b = Buildings(Note("人物/田", "別名: 田\n"), Note("日記", "# 日\n田中が来た。山田。田。 田んぼ。a田b。\n"));
            Assert.AreEqual(1, b.Length);
            // 「田。」「田んぼ」「a田b」は検出される(「山田」「田中」は検出されない)。
            Assert.AreEqual(3, b[0].Count);
        }

        [Test]
        public void 同じ別名を二つのノートが宣言したら_どちらにも建てない()
        {
            Assert.AreEqual(0, Buildings(Note("a", "別名: 田中\n"), Note("b", "別名: 田中, 鈴木\n"), Note("日記", "# 日\n田中。\n")).Length);
            var b = Buildings(Note("a", "別名: 田中\n"), Note("b", "別名: 田中, 鈴木\n"), Note("日記", "# 日\n田中。鈴木。\n"));
            Assert.AreEqual("b", b.Single().ProperNounNote.Value);
        }

        [Test]
        public void 別名がなければ固有名詞ノートではなく_空の別名の行は普通の本文()
        {
            Assert.AreEqual(0, Buildings(Note("a", "別名:\n# A\n"), Note("日記", "# 日\n別名\n")).Length);
        }

        [Test]
        public void 同じ言葉が複数のノートの拠点に出れば_その数だけ建つ()
        {
            var b = Buildings(Tanaka, Note("x", "# X\n田中\n"), Note("y", "# Y\n田中\n"));
            CollectionAssert.AreEqual(new[] { "x#X", "y#Y" }, b.Select(v => v.Node).ToArray());
        }
    }
}
