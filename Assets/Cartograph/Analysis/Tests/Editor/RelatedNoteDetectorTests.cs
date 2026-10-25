using System.Linq;
using NUnit.Framework;

namespace Cartograph.Analysis.Tests
{
    public class RelatedNoteDetectorTests
    {
        private static RelatedNoteSource Note(string id, string title, string body) => new RelatedNoteSource(id, title, body);

        private static EmotionLexicon Lex()
        {
            var lex = EmotionLexicon.FromCsv("嬉し,喜\n怒り,怒\n悲し,哀\n楽し,楽\n", out var errors);
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            return lex;
        }

        private static RelatedNoteCandidate Find(System.Collections.Generic.IReadOnlyList<RelatedNoteCandidate> list, string a, string b)
            => list.FirstOrDefault(c => c.Pair.Equals(new RelatedPair(a, b)));

        [Test]
        public void 本文にほかのノート名が出てくると_言及として候補になる()
        {
            var r = new RelatedNoteDetector().Detect(new[]
            {
                Note("旅行", "京都旅行", "# 京都旅行\n去年の秋に清水寺へ行った。\n"),
                Note("寺", "清水寺", "# 清水寺\n"),
                Note("無関係", "数学", "# 数学\n微分と積分。\n"),
            });
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual("寺", r[0].From);
            Assert.AreEqual("旅行", r[0].To);
            Assert.AreEqual(RelatedReasonKind.Mention, r[0].Reasons[0].Kind);
            StringAssert.Contains("清水寺", r[0].Reasons[0].Label);
        }

        [Test]
        public void 別名でも言及になる_全角英数と大文字小文字はそろえる()
        {
            var r = new RelatedNoteDetector().Detect(new[]
            {
                Note("a", "日記", "# 日記\n今日はＵｎｉｔｙで遊んだ。\n"),
                Note("unity", "ゲームエンジン", "別名: Unity, ユニティ\n# ゲームエンジン\n"),
            });
            Assert.IsNotNull(Find(r, "a", "unity"));
        }

        [Test]
        public void 共通キーワードが2語以上あると候補_1語だけならしきい値未満()
        {
            var one = new RelatedNoteDetector().Detect(new[]
            {
                Note("a", "A", "# A\n東京の天気。\n"),
                Note("b", "B", "# B\n東京の人口。\n"),
            });
            Assert.AreEqual(0, one.Count);

            var two = new RelatedNoteDetector().Detect(new[]
            {
                Note("a", "A", "# A\n東京の天気と気温。\n"),
                Note("b", "B", "# B\n東京の人口と気温。\n"),
            });
            Assert.AreEqual(1, two.Count);
            Assert.AreEqual(RelatedReasonKind.Keyword, two[0].Reasons[0].Kind);
            StringAssert.Contains("東京", two[0].Reasons[0].Label);
            StringAssert.Contains("気温", two[0].Reasons[0].Label);
        }

        [Test]
        public void 多くのノートに出るありふれた語はキーワードにしない()
        {
            var notes = new[]
            {
                Note("a", "A", "# A\n今日 東京 天気\n"),
                Note("b", "B", "# B\n今日 東京 人口\n"),
                Note("c", "C", "# C\n今日 大阪 食事\n"),
                Note("d", "D", "# D\n今日 札幌 雪\n"),
            };
            var r = new RelatedNoteDetector(new RelatedNoteOptions { Threshold = 1.0 }).Detect(notes);
            // 「今日」は 4 ノート中 4(>50%)で外れる。「東京」(2/4)だけが残る
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual("a", r[0].From);
            Assert.AreEqual("b", r[0].To);
            StringAssert.DoesNotContain("今日", r[0].Reasons[0].Label);
        }

        [Test]
        public void 同じ固有名詞ノートに触れる2つのノートは_固有名詞が共通として候補になる()
        {
            var notes = new[]
            {
                Note("x", "出張", "# 出張\n田中と会議をした。\n"),
                Note("y", "飲み会", "# 飲み会\nたなかも来た。\n"),
                Note("田中", "田中さん", "別名: 田中, たなか\n# 田中さん\n同僚。\n"),
            };
            // 固有名詞だけ(1.5 点)では既定のしきい値(2.0)に届かない
            Assert.IsNull(Find(new RelatedNoteDetector().Detect(notes), "x", "y"));
            var r = new RelatedNoteDetector(new RelatedNoteOptions { Threshold = 1.0 }).Detect(notes);
            var c = Find(r, "x", "y");
            Assert.IsNotNull(c);
            Assert.AreEqual(RelatedReasonKind.ProperNoun, c.Reasons[0].Kind);
            StringAssert.Contains("田中さん", c.Reasons[0].Label);
            StringAssert.Contains("固有名詞", c.Reasons[0].Label);
        }

        [Test]
        public void コードブロックとインラインコードは根拠にしない()
        {
            var r = new RelatedNoteDetector().Detect(new[]
            {
                Note("a", "A", "# A\n```\n清水寺 東京 気温\n```\n`清水寺`\n"),
                Note("寺", "清水寺", "# 清水寺\n東京 気温\n"),
            });
            Assert.AreEqual(0, r.Count);
        }

        [Test]
        public void 感情の傾向だけでは既定のしきい値に届かない_足すと候補になる()
        {
            var a = Note("a", "A", "# A\n嬉しい。楽しい。嬉しい。\n");
            var b = Note("b", "B", "# B\n楽しい。嬉しい。楽しい。\n");
            var only = new RelatedNoteDetector(null, Lex()).Detect(new[] { a, b });
            Assert.AreEqual(0, only.Count);

            var lowBar = new RelatedNoteDetector(new RelatedNoteOptions { Threshold = 0.4 }, Lex()).Detect(new[] { a, b });
            Assert.AreEqual(1, lowBar.Count);
            Assert.AreEqual(RelatedReasonKind.Emotion, lowBar[0].Reasons[0].Kind);

            // 辞書がなければ感情は見ない
            Assert.AreEqual(0, new RelatedNoteDetector(new RelatedNoteOptions { Threshold = 0.4 }).Detect(new[] { a, b }).Count);
        }

        [Test]
        public void 感情の傾向が逆なら類似にならない_否定された語は数えない()
        {
            var opt = new RelatedNoteOptions { Threshold = 0.1 };
            var joy = Note("a", "A", "# A\n嬉しい。嬉しい。嬉しい。\n");
            var anger = Note("b", "B", "# B\n怒り。怒り。怒り。\n");
            Assert.AreEqual(0, new RelatedNoteDetector(opt, Lex()).Detect(new[] { joy, anger }).Count);

            var negated = Note("c", "C", "# C\n嬉しくない。嬉しくない。嬉しくない。\n");
            Assert.AreEqual(0, new RelatedNoteDetector(opt, Lex()).Detect(new[] { joy, negated }).Count);
        }

        [Test]
        public void 重みとしきい値は外から調整できる()
        {
            var notes = new[]
            {
                Note("a", "A", "# A\n東京の天気。\n"),
                Note("b", "B", "# B\n東京の人口。\n"),
            };
            Assert.AreEqual(0, new RelatedNoteDetector().Detect(notes).Count);
            Assert.AreEqual(1, new RelatedNoteDetector(new RelatedNoteOptions { KeywordWeight = 2.0 }).Detect(notes).Count);
            Assert.AreEqual(1, new RelatedNoteDetector(new RelatedNoteOptions { Threshold = 1.0 }).Detect(notes).Count);
        }

        [Test]
        public void 除外した組は候補に出ない_向きは問わない()
        {
            var notes = new[]
            {
                Note("a", "A", "# A\n清水寺へ行った。\n"),
                Note("b", "清水寺", "# 清水寺\n"),
            };
            var detector = new RelatedNoteDetector();
            Assert.AreEqual(1, detector.Detect(notes).Count);
            Assert.AreEqual(0, detector.Detect(notes, new[] { new RelatedPair("b", "a") }).Count);
            Assert.AreEqual(0, detector.DetectFor("a", notes, new[] { new RelatedPair("a", "b") }).Count);
        }

        [Test]
        public void 点の高い順に並び_理由も点の高い順になる()
        {
            var r = new RelatedNoteDetector().Detect(new[]
            {
                Note("a", "A", "# A\n清水寺へ行った。東京の天気と気温。\n"),
                Note("b", "清水寺", "# 清水寺\n東京の人口と気温。\n"),
                Note("c", "C", "# C\n東京の天気と気温。\n"),
            });
            Assert.GreaterOrEqual(r.Count, 2);
            for (int i = 1; i < r.Count; i++) Assert.GreaterOrEqual(r[i - 1].Score, r[i].Score);
            var top = r[0];
            Assert.AreEqual(top.Reasons.Sum(x => x.Score), top.Score, 1e-9);
            for (int i = 1; i < top.Reasons.Count; i++) Assert.GreaterOrEqual(top.Reasons[i - 1].Score, top.Reasons[i].Score);
        }

        [Test]
        public void DetectForは1ノートに関わる組だけを返し_全件の結果と一致する()
        {
            var notes = new[]
            {
                Note("a", "A", "# A\n清水寺へ行った。\n"),
                Note("b", "清水寺", "# 清水寺\n東京の天気と気温。\n"),
                Note("c", "C", "# C\n東京の人口と気温。\n"),
                Note("d", "D", "# D\n微分積分。\n"),
            };
            var detector = new RelatedNoteDetector();
            var all = detector.Detect(notes);
            var forB = detector.DetectFor("b", notes);
            Assert.IsTrue(forB.All(c => c.Involves("b")));
            Assert.AreEqual(all.Count(c => c.Involves("b")), forB.Count);
            Assert.AreEqual(0, detector.DetectFor("存在しない", notes).Count);
            Assert.AreEqual(0, detector.DetectFor("d", notes).Count);
        }

        [Test]
        public void Replaceは変更したノートに関わる候補だけを入れ替える()
        {
            var before = new[]
            {
                Note("a", "A", "# A\n清水寺へ行った。\n"),
                Note("b", "清水寺", "# 清水寺\n"),
                Note("c", "C", "# C\n東京の天気と気温。\n"),
                Note("d", "D", "# D\n東京の人口と気温。\n"),
            };
            var detector = new RelatedNoteDetector();
            var existing = detector.Detect(before);
            Assert.AreEqual(2, existing.Count);

            // a から清水寺の言及を消す
            var after = new[] { Note("a", "A", "# A\n何もない。\n"), before[1], before[2], before[3] };
            var merged = RelatedNoteDetector.Replace(existing, "a", detector.DetectFor("a", after));
            Assert.AreEqual(1, merged.Count);
            Assert.IsNotNull(Find(merged, "c", "d"));
            CollectionAssert.AreEqual(detector.Detect(after).Select(c => c.Pair), merged.Select(c => c.Pair));
        }

        [Test]
        public void 入力が空_重複ID_nullでも落ちない()
        {
            var detector = new RelatedNoteDetector();
            Assert.AreEqual(0, detector.Detect(null).Count);
            Assert.AreEqual(0, detector.Detect(new RelatedNoteSource[0]).Count);
            Assert.AreEqual(0, detector.Detect(new[] { Note("a", null, null) }).Count);
            Assert.AreEqual(0, detector.Detect(new[] { Note("a", "A", "x"), Note("a", "A", "y") }).Count);
            Assert.Throws<System.ArgumentException>(() => new RelatedNoteSource("", "t", "b"));
        }
    }
}
