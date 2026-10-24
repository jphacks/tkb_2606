using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Cartograph.Core.Tests
{
    public class LabeledTextTests
    {
        [Test]
        public void Insert_Delete_Sliceで来歴が正しく動く()
        {
            var t = new LabeledText("abc", Origin.Unknown);
            t.Insert(1, "XY", LabeledSlice.Uniform(2, Origin.Typed));
            Assert.AreEqual("aXYbc", t.Text);
            Assert.AreEqual("U1T2U2", t.EncodeLabels());

            var removed = t.Delete(2, 2);          // "Yb"
            Assert.AreEqual("aXc", t.Text);
            Assert.AreEqual("T1U1", removed.Encode());
            Assert.AreEqual("U1T1U1", t.EncodeLabels());

            Assert.AreEqual("T1U1", t.Slice(1, 2).Encode());
        }

        [Test]
        public void 本文量は空白と改行を数えず_typedだけを数える()
        {
            var t = new LabeledText("", Origin.Unknown);
            t.Insert(0, "a b\n　c", LabeledSlice.Uniform(6, Origin.Typed));   // 全角空白を含む
            t.Insert(6, "貼り付け", LabeledSlice.Uniform(4, Origin.Unknown));
            Assert.AreEqual(3, t.TypedCount());
            Assert.AreEqual(1, t.TypedCount(0, 2));
        }

        [Test]
        public void Encode_Decodeの往復と不正な入力()
        {
            Assert.IsTrue(LabeledSlice.TryDecode("T3U2T5", 10, out var s));
            Assert.AreEqual("T3U2T5", s.Encode());
            Assert.IsTrue(LabeledSlice.TryDecode("", 0, out var empty));
            Assert.AreEqual(0, empty.Length);

            Assert.IsFalse(LabeledSlice.TryDecode("T3", 4, out _), "長さが合わない");
            Assert.IsFalse(LabeledSlice.TryDecode("T0", 0, out _), "長さ 0 の区間");
            Assert.IsFalse(LabeledSlice.TryDecode("X3", 3, out _), "未知のラベル");
            Assert.IsFalse(LabeledSlice.TryDecode("T", 1, out _), "長さがない");
            Assert.IsFalse(LabeledSlice.TryDecode("T99999999999", 5, out _), "桁あふれ");
        }

        [Test]
        public void 範囲外の操作は例外になる()
        {
            var t = new LabeledText("abc", Origin.Typed);
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Delete(2, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Insert(4, "x", LabeledSlice.Uniform(1, Origin.Typed)));
            Assert.Throws<ArgumentException>(() => t.Insert(0, "xy", LabeledSlice.Uniform(1, Origin.Typed)));
        }

        [Test]
        public void ランダム操作が1文字ずつのモデルと一致する()
        {
            var rnd = new Random(12345);
            var t = new LabeledText("", Origin.Unknown);
            var model = new List<(char c, Origin o)>();
            for (int step = 0; step < 3000; step++)
            {
                if (model.Count == 0 || rnd.Next(3) != 0)
                {
                    int pos = rnd.Next(model.Count + 1);
                    int len = rnd.Next(1, 6);
                    var chars = new string(Enumerable.Range(0, len).Select(_ => (char)('a' + rnd.Next(26))).ToArray());
                    var labels = new List<Run>();
                    var origins = new List<Origin>();
                    for (int i = 0; i < len; i++)
                    {
                        var o = rnd.Next(2) == 0 ? Origin.Typed : Origin.Unknown;
                        origins.Add(o);
                        labels.Add(new Run(1, o));
                    }
                    t.Insert(pos, chars, new LabeledSlice(labels));
                    model.InsertRange(pos, chars.Select((c, i) => (c, origins[i])));
                }
                else
                {
                    int pos = rnd.Next(model.Count);
                    int len = rnd.Next(0, Math.Min(6, model.Count - pos) + 1);
                    var removed = t.Delete(pos, len);
                    Assert.AreEqual(len, removed.Length);
                    model.RemoveRange(pos, len);
                }
                Assert.AreEqual(model.Count, t.Length);
            }
            Assert.AreEqual(new string(model.Select(m => m.c).ToArray()), t.Text);
            var lab = t.Labels;
            for (int i = 0; i < model.Count; i++) Assert.AreEqual(model[i].o, lab.OriginAt(i), "位置 " + i);
        }
    }

    public class TextDiffTests
    {
        [Test]
        public void 末尾への追加()
        {
            var d = TextDiff.Compute("あいう", "あいうえ");
            Assert.AreEqual(3, d.Pos);
            Assert.AreEqual("え", d.Inserted);
            Assert.AreEqual("", d.Deleted);
        }

        [Test]
        public void バックスペースは削除として取れる()
        {
            var d = TextDiff.Compute("あいう", "あう", 2, 2);
            Assert.AreEqual(1, d.Pos);
            Assert.AreEqual("い", d.Deleted);
            Assert.AreEqual("", d.Inserted);
        }

        [Test]
        public void 同じ文字が続く時は選択範囲の位置に合わせる()
        {
            var plain = TextDiff.Compute("aa", "aaa");
            Assert.AreEqual(2, plain.Pos, "手がかりなしなら末尾側");
            var anchored = TextDiff.Compute("aa", "aaa", 0, 0);
            Assert.AreEqual(0, anchored.Pos);
            Assert.AreEqual("a", anchored.Inserted);
        }

        [Test]
        public void 選択範囲を打ち替えると置換として取れる()
        {
            var d = TextDiff.Compute("abcd", "aXd", 1, 3);
            Assert.AreEqual(1, d.Pos);
            Assert.AreEqual("bc", d.Deleted);
            Assert.AreEqual("X", d.Inserted);
        }

        [Test]
        public void 同じ内容を打ち直しただけなら変更なし()
        {
            Assert.IsTrue(TextDiff.Compute("abc", "abc", 0, 3).IsEmpty);
            Assert.IsTrue(TextDiff.Compute("abc", "abc").IsEmpty);
        }

        [Test]
        public void サロゲートペアを途中で割らない()
        {
            string emoji = "😀";   // U+1F600
            var d = TextDiff.Compute("a" + emoji + "b", "a" + "😁" + "b");
            Assert.AreEqual(1, d.Pos);
            Assert.AreEqual(emoji, d.Deleted);
            Assert.AreEqual("😁", d.Inserted);

            var del = TextDiff.Compute("a" + emoji, "a");
            Assert.AreEqual(emoji, del.Deleted);

            var ins = TextDiff.Compute("a", "a" + emoji);
            Assert.AreEqual(emoji, ins.Inserted);
        }

        [Test]
        public void 手がかりが食い違っても最小の差分にもどる()
        {
            var d = TextDiff.Compute("hello world", "hello brave world", 0, 0);
            Assert.AreEqual("brave ", d.Inserted);
            Assert.AreEqual(6, d.Pos);
        }
    }

    public class EventJsonTests
    {
        private static void RoundTrip(EditEvent e)
        {
            string line = EventJson.Serialize(e);
            Assert.IsFalse(line.Contains("\n"), "1 行に収まる");
            Assert.IsTrue(EventJson.TryParse(line, out var back), line);
            Assert.AreEqual(e.Seq, back.Seq);
            Assert.AreEqual(e.T, back.T);
            Assert.AreEqual(e.Type, back.Type);
            Assert.AreEqual(e.Pos, back.Pos);
            Assert.AreEqual(e.Text, back.Text);
            Assert.AreEqual(e.Len, back.Len);
            Assert.AreEqual(e.Src, back.Src);
            Assert.AreEqual(e.From, back.From);
            Assert.AreEqual(e.SrcDoc ?? "", back.SrcDoc ?? "");
            Assert.AreEqual(e.Lbl ?? "", back.Lbl ?? "");
        }

        [Test]
        public void 特殊な文字を含むイベントが往復できる()
        {
            RoundTrip(new EditEvent { Seq = 1, T = 1761270000123, Type = EventType.Insert, Pos = 342, Text = "波", Len = 1, Src = "ime" });
            RoundTrip(new EditEvent { Seq = 2, T = 5, Type = EventType.Insert, Pos = 0, Src = "key", Len = 16,
                Text = "引用\"と\\と\n改行\tタブ\r\u0001😀" });
            RoundTrip(new EditEvent { Seq = 3, T = 5, Type = EventType.Insert, Pos = 0, Src = "internal", Len = 1,
                Text = "\ud83d", From = 7, SrcDoc = "大学用/授業/ノート", Lbl = "T1" });   // 対のない上位サロゲート
            RoundTrip(new EditEvent { Seq = 4, T = 5, Type = EventType.Delete, Pos = 2, Len = 3, Src = "key" });
            RoundTrip(new EditEvent { Seq = 5, T = 5, Type = EventType.Move, Pos = 2, Len = 3, From = 9, Src = "key" });
            RoundTrip(new EditEvent { Seq = 6, T = 5, Type = EventType.Blocked, Pos = 2, Len = 40, Src = "internal" });
        }

        [Test]
        public void 仕様書の例の行を読める()
        {
            const string line = "{\"seq\": 128, \"t\": 1761270000123, \"type\": \"insert\", \"pos\": 342, \"text\": \"波\", \"src\": \"ime\"}";
            Assert.IsTrue(EventJson.TryParse(line, out var e));
            Assert.AreEqual(128, e.Seq);
            Assert.AreEqual("波", e.Text);
            Assert.AreEqual(-1, e.From);
        }

        [Test]
        public void insertのlenは出さずblockedに本文は出ない()
        {
            string ins = EventJson.Serialize(new EditEvent { Seq = 1, T = 1, Type = EventType.Insert, Pos = 0, Text = "x", Src = "key" });
            StringAssert.DoesNotContain("\"len\"", ins);
            string blk = EventJson.Serialize(new EditEvent { Seq = 2, T = 1, Type = EventType.Blocked, Pos = 0, Len = 5, Src = "key" });
            StringAssert.DoesNotContain("\"text\"", blk);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"seq\":1,\"t\":1,\"type\":\"insert\",\"pos\":0")]                       // 途中で切れた行
        [TestCase("{\"seq\":0,\"t\":1,\"type\":\"delete\",\"pos\":0,\"len\":1}")]           // seq は 1 から
        [TestCase("{\"seq\":1,\"t\":1,\"type\":\"explode\",\"pos\":0,\"len\":1}")]          // 未知の種別
        [TestCase("{\"seq\":1,\"t\":1,\"type\":\"insert\",\"pos\":0}")]                      // text がない
        [TestCase("{\"seq\":1,\"t\":1,\"type\":\"delete\",\"pos\":-1,\"len\":1}")]          // 負の位置
        [TestCase("{\"seq\":1.5,\"t\":1,\"type\":\"delete\",\"pos\":0,\"len\":1}")]         // 小数
        [TestCase("{\"seq\":1,\"t\":1,\"type\":\"delete\",\"pos\":0,\"len\":1,\"x\":[1]}")] // 配列は扱わない
        public void 壊れた行は失敗になる(string line)
        {
            Assert.IsFalse(EventJson.TryParse(line, out _));
        }

        [Test]
        public void 未知のキーは無視する()
        {
            const string line = "{\"seq\":1,\"t\":1,\"type\":\"delete\",\"pos\":0,\"len\":1,\"future\":\"x\"}";
            Assert.IsTrue(EventJson.TryParse(line, out var e));
            Assert.AreEqual(1, e.Len);
        }

        [Test]
        public void スナップショットが往復できる()
        {
            var doc = new LabeledText("日本語\n\"x\"", Origin.Unknown);
            doc.Insert(3, "ab", LabeledSlice.Uniform(2, Origin.Typed));
            var snap = Snapshot.Of(1000, doc);
            Assert.IsTrue(Snapshot.TryParse(snap.Serialize(), out var back));
            Assert.AreEqual(1000, back.Seq);
            Assert.IsTrue(back.TryToLabeledText(out var restored));
            Assert.AreEqual(doc.Text, restored.Text);
            Assert.AreEqual(doc.EncodeLabels(), restored.EncodeLabels());
        }
    }

    public class NotePathTests
    {
        [TestCase("ノート")]
        [TestCase("大学用/授業/科目A/ノート")]
        [TestCase("a b/c.d")]
        public void 使える名前(string s) => Assert.IsTrue(NotePath.TryParse(s, out _));

        [TestCase("")]
        [TestCase("../外へ")]
        [TestCase("a/../b")]
        [TestCase("/絶対")]
        [TestCase("a//b")]
        [TestCase("a/")]
        [TestCase("C:/x")]
        [TestCase("a\\b")]
        [TestCase("a:b")]
        [TestCase("con")]
        [TestCase("nul.txt")]
        [TestCase("末尾ドット.")]
        [TestCase("末尾空白 ")]
        [TestCase("a?b")]
        public void 使えない名前(string s) => Assert.IsFalse(NotePath.TryParse(s, out _), s);

        [Test]
        public void 名前とフォルダを取り出せる()
        {
            var p = NotePath.Parse("大学用/授業/科目A/ノート");
            Assert.AreEqual("ノート", p.Name);
            Assert.AreEqual("大学用/授業/科目A", p.FolderPath);
            Assert.AreEqual("大学用/授業/科目A/別名", p.WithName("別名").Value);
            Assert.AreEqual("", NotePath.Parse("ルート").FolderPath);
            Assert.Throws<ArgumentException>(() => p.WithName("../x"));
        }

        [Test]
        public void 保存先はプロジェクトの規則どおり()
        {
            var env = new Env();
            var p = NotePath.Parse("大学用/授業/ノート");
            string rel = System.IO.Path.Combine("大学用", "授業", "ノート");
            Assert.AreEqual(System.IO.Path.Combine(env.Paths.Root, "notes", rel + ".md"), env.Paths.NoteFile(p));
            Assert.AreEqual(System.IO.Path.Combine(env.Paths.Root, ".cartograph", "history", rel + ".jsonl"), env.Paths.HistoryFile(p));
            Assert.Throws<ArgumentException>(() => new ProjectPaths("relative/path"));
        }
    }
}
