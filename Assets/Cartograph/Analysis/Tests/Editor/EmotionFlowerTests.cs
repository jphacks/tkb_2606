using System.Linq;
using Cartograph.Core;
using NUnit.Framework;

namespace Cartograph.Analysis.Tests
{
    public class EmotionFlowerTests
    {
        private static EmotionLexicon Lex(string csv)
        {
            var lex = EmotionLexicon.FromCsv(csv, out var errors);
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            return lex;
        }

        private static NoteAnalysis Parse(string text, EmotionLexicon lex, Origin origin = Origin.Typed)
            => NoteParser.Parse(new AnalysisInput(NotePath.Parse("n"), new LabeledText(text, origin), 1), lex);

        private static readonly string Csv = "嬉し,喜\n怒り,怒\n悲し,哀\n楽し,楽\n悲しみ,哀\n";

        [Test]
        public void 感情語を書くと_その拠点に花が咲く()
        {
            var r = Parse("# A\n今日は嬉しい。\n## B\n怒りを感じた\n", Lex(Csv));

            Assert.AreEqual(2, r.Flowers.Count);
            Assert.AreEqual("n#A", r.Flowers[0].Node);
            Assert.AreEqual(2, r.Flowers[0].Line);
            Assert.AreEqual("嬉し", r.Flowers[0].Word);
            Assert.AreEqual(Emotion.Joy, r.Flowers[0].Emotion);
            Assert.IsFalse(r.Flowers[0].Withered);
            Assert.AreEqual("n#A/B", r.Flowers[1].Node);
            Assert.AreEqual(Emotion.Anger, r.Flowers[1].Emotion);
        }

        [Test]
        public void 辞書がなければ花は咲かない()
        {
            Assert.AreEqual(0, Parse("# A\n嬉しい\n", null).Flowers.Count);
            Assert.AreEqual(0, Parse("# A\n嬉しい\n", EmotionLexicon.Empty).Flowers.Count);
        }

        [Test]
        public void 最長一致で探す()
        {
            var r = Parse("# A\n悲しみ\n", Lex(Csv));
            Assert.AreEqual(1, r.Flowers.Count);
            Assert.AreEqual("悲しみ", r.Flowers[0].Word);
        }

        [Test]
        public void typedでない文字は対象にしない()
        {
            var doc = new LabeledText("", Origin.Unknown);
            string s = "# A\n嬉しい 怒り\n";
            doc.Insert(0, s, LabeledSlice.Uniform(s.Length, Origin.Typed));
            int at = s.IndexOf("怒り");
            doc.Delete(at, 2);
            doc.Insert(at, "怒り", LabeledSlice.Uniform(2, Origin.Unknown));      // 貼り付けた文字

            var r = NoteParser.Parse(new AnalysisInput(NotePath.Parse("n"), doc, 1), Lex(Csv));
            Assert.AreEqual(new[] { "嬉し" }, r.Flowers.Select(f => f.Word).ToArray());
            Assert.AreEqual(0, Parse("# A\n嬉しい\n", Lex(Csv), Origin.Unknown).Flowers.Count);
        }

        [Test]
        public void 語の一部だけがtypedのときは咲かない()
        {
            var doc = new LabeledText("", Origin.Unknown);
            string s = "# A\n怒り\n";
            doc.Insert(0, s, LabeledSlice.Uniform(s.Length, Origin.Typed));
            doc.Delete(s.IndexOf("り"), 1);
            doc.Insert(s.IndexOf("怒") + 1, "り", LabeledSlice.Uniform(1, Origin.Unknown));

            var r = NoteParser.Parse(new AnalysisInput(NotePath.Parse("n"), doc, 1), Lex(Csv));
            Assert.AreEqual(0, r.Flowers.Count);
        }

        [Test]
        public void 否定が続くと枯れた花になる()
        {
            var r = Parse("# A\n嬉しくない。怒りません。楽しくなかった。悲しい\n", Lex(Csv));

            Assert.AreEqual(new[] { true, true, true, false }, r.Flowers.Select(f => f.Withered).ToArray());
        }

        [Test]
        public void 同じ文の同じ語は1輪にまとめ_文が違えば別の花()
        {
            var r = Parse("# A\n嬉しい嬉しい嬉しい。嬉しい！嬉しいし怒りも怒りも\n", Lex(Csv));

            // 1 文目(。まで)・2 文目(！まで)・3 文目に 1 輪ずつ。3 文目の「怒り」の繰り返しは 1 輪にまとまる。
            Assert.AreEqual(3, r.Flowers.Count(f => f.Word == "嬉し"));
            Assert.AreEqual(1, r.Flowers.Count(f => f.Word == "怒り"));
        }

        [Test]
        public void 文の区切り_同じ行で句点をはさむと別の花()
        {
            var r = Parse("# A\n嬉しい。嬉しい。嬉しい嬉しい\n", Lex(Csv));
            Assert.AreEqual(3, r.Flowers.Count);

            var r2 = Parse("# A\n嬉しい\n嬉しい\n", Lex(Csv));
            Assert.AreEqual(2, r2.Flowers.Count);       // 行が違えば別の花
        }

        [Test]
        public void コードブロックと見出し行は対象にしない()
        {
            var r = Parse("# 嬉しい\n```\n怒り\n```\n悲しい\n", Lex(Csv));

            Assert.AreEqual(new[] { "悲し" }, r.Flowers.Select(f => f.Word).ToArray());
        }

        [Test]
        public void 見出しより前の感情語は_最初の見出しの所属になる()
        {
            var r = Parse("楽しい\n# A\n", Lex(Csv));
            Assert.AreEqual("n#A", r.Flowers[0].Node);
            Assert.AreEqual(1, r.Flowers[0].Line);
        }

        [Test]
        public void 見出しのないファイルでも咲く()
        {
            var r = Parse("悲しい\n", Lex(Csv));
            Assert.AreEqual("n#", r.Flowers[0].Node);
        }

        [Test]
        public void ProjectAnalyzerの結果に花が集約される()
        {
            var inputs = new[]
            {
                new AnalysisInput(NotePath.Parse("a"), new LabeledText("# A\n嬉しい\n", Origin.Typed), 1),
                new AnalysisInput(NotePath.Parse("b"), new LabeledText("# B\n怒り\n", Origin.Typed), 1),
            };

            var r = ProjectAnalyzer.Analyze(inputs, Lex(Csv));
            Assert.AreEqual(new[] { "a#A", "b#B" }, r.Flowers.Select(f => f.Node).ToArray());
            Assert.AreEqual(0, ProjectAnalyzer.Analyze(inputs).Flowers.Count);
        }

        [Test]
        public void 感情語を消すと花も消える()
        {
            var lex = Lex(Csv);
            Assert.AreEqual(1, Parse("# A\n嬉しい\n", lex).Flowers.Count);
            Assert.AreEqual(0, Parse("# A\nうれしい\n", lex).Flowers.Count);
        }

        [Test]
        public void CSVの読み込み_コメントと空行と不正な行()
        {
            var lex = EmotionLexicon.FromCsv(
                "﻿# コメント\n\n嬉し , 喜\njoyful,JOY\n短,喜\n謎,不明\n行だけ\n嬉し,怒\n怒り,怒\n", out var errors);

            Assert.AreEqual(3, lex.Count);                  // 嬉し、joyful、怒り
            Assert.AreEqual(4, errors.Count);               // 短い語、不明な分類、列不足、分類違いの重複
            StringAssert.Contains("5 行目", errors[0]);
            Assert.AreEqual(Emotion.Joy, Parse("# A\n嬉しい\n", lex).Flowers[0].Emotion);
        }

        [Test]
        public void 一文字の語は辞書に入れられない()
        {
            Assert.Throws<System.ArgumentException>(() => new EmotionLexicon(
                new[] { new System.Collections.Generic.KeyValuePair<string, Emotion>("怒", Emotion.Anger) }));
        }

        private static string[] DefaultWords(string body) =>
            Parse("# A\n" + body + "\n", EmotionLexicon.Default).Flowers.Select(f => f.Word).ToArray();

        [Test]
        public void 初版の辞書_別の語の一部に当たる文では花が咲かない()
        {
            foreach (var body in new[]
            {
                "大切な人。親切な対応。適切な処理。",            // 切な
                "音楽します。娯楽として。洋楽と邦楽。",          // 楽し
                "不安定な状態。不安全な足場。",                  // 不安
                "放っておく。ほっとく。",                        // ほっと
                "つららが下がる。つらなる山。",                  // つら
                "上空しか見えない。",                            // 空し
            })
            {
                Assert.AreEqual(0, DefaultWords(body).Length, body);
            }
        }

        [Test]
        public void 初版の辞書_除外語の後ろの本物の感情語は咲く()
        {
            CollectionAssert.AreEqual(new[] { "楽し" }, DefaultWords("音楽を楽しむ"));
            CollectionAssert.AreEqual(new[] { "不安" }, DefaultWords("不安定だが不安もある"));
        }

        [Test]
        public void 初版の辞書_活用した形と否定にも当たる()
        {
            var r = Parse("# A\n嬉しかった。悲しみ。楽しかった。切ない。つらくない。面白くない。\n", EmotionLexicon.Default);

            CollectionAssert.AreEqual(new[] { "嬉し", "悲し", "楽し", "切ない", "つらく", "面白" }, r.Flowers.Select(f => f.Word).ToArray());
            CollectionAssert.AreEqual(new[] { false, false, false, false, true, true }, r.Flowers.Select(f => f.Withered).ToArray());
        }

        [Test]
        public void 除外語は_typedでなくても読み飛ばされ_分類は除外と書く()
        {
            var lex = EmotionLexicon.FromCsv("楽し,楽\n音楽,除外\n", out var errors);
            Assert.AreEqual(0, errors.Count);
            Assert.AreEqual(1, lex.Count);
            Assert.AreEqual(1, lex.ExcludedCount);
            Assert.AreEqual(0, Parse("# A\n音楽します\n", lex).Flowers.Count);
            Assert.AreEqual(0, Parse("# A\n音楽します\n", lex, Origin.Unknown).Flowers.Count);
        }

        [Test]
        public void 初版の辞書_読み込みに問題がなく_重複もない()
        {
            var lines = typeof(EmotionLexicon).Assembly.GetType("Cartograph.Analysis.DefaultEmotionWords")
                .GetField("Csv").GetRawConstantValue().ToString()
                .Split('\n').Where(l => l.Length > 0 && l[0] != '#').ToArray();
            var lex = EmotionLexicon.FromCsv(string.Join("\n", lines), out var errors);

            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            Assert.AreEqual(lines.Length, lex.Count + lex.ExcludedCount);     // 重複で落ちた語がない
            Assert.Greater(lex.ExcludedCount, 0);
        }

        [Test]
        public void 初版の辞書が読み込め_4種すべての語がある()
        {
            var lex = EmotionLexicon.FromCsv(@"嬉し,喜", out _);
            Assert.AreEqual(1, lex.Count);

            var def = EmotionLexicon.Default;
            Assert.Greater(def.Count, 40);
            string text = "# A\n嬉しい。怒りだ。悲しい。楽しい。\n";
            var emotions = Parse(text, def).Flowers.Select(f => f.Emotion).ToArray();
            CollectionAssert.AreEqual(new[] { Emotion.Joy, Emotion.Anger, Emotion.Sorrow, Emotion.Pleasure }, emotions);
        }
    }
}
