using System.Linq;
using NUnit.Framework;

namespace Cartograph.Core.Tests
{
    public class InputReviewerTests
    {
        private static ChangeCandidate Cand(string newText, int s, int e, InputEvidence ev)
            => new ChangeCandidate { NewText = newText, SelStartBefore = s, SelEndBefore = e, Evidence = ev };

        private static InputEvidence Ev(EvidenceKind k, string text = null) => new InputEvidence { Kind = k, Text = text };

        [Test]
        public void キー入力の1文字は許可されtypedになる()
        {
            var r = InputReviewer.Review("あい", "n", Cand("あいう", 2, 2, Ev(EvidenceKind.Key, "う")), null);
            Assert.IsTrue(r.Approved);
            Assert.AreEqual(1, r.Ops.Count);
            Assert.AreEqual(EventType.Insert, r.Ops[0].Type);
            Assert.AreEqual(EventSource.Key, r.Ops[0].Src);
            Assert.IsTrue(r.Ops[0].Labels.IsUniform(Origin.Typed));
        }

        [Test]
        public void キーの証拠と挿入が食い違えば遮断される()
        {
            // 「う」を打ったと申告しながら、実際には長い文字列が入った(外部の貼り付けが経路を偽った)
            var r = InputReviewer.Review("あい", "n", Cand("あい外部の文章", 2, 2, Ev(EvidenceKind.Key, "う")), null);
            Assert.IsFalse(r.Approved);
            Assert.AreEqual("あい", r.RevertText);
            Assert.AreEqual(InputReviewer.BlockedMessage, r.Reason);
            Assert.AreEqual(EventType.Blocked, r.Ops.Single().Type);
        }

        [Test]
        public void キーの証拠が2文字以上なら遮断される()
        {
            var r = InputReviewer.Review("", "n", Cand("ab", 0, 0, Ev(EvidenceKind.Key, "ab")), null);
            Assert.IsFalse(r.Approved);
        }

        [Test]
        public void サロゲートペア1文字のキー入力は許可される()
        {
            var r = InputReviewer.Review("", "n", Cand("😀", 0, 0, Ev(EvidenceKind.Key, "😀")), null);
            Assert.IsTrue(r.Approved);
        }

        [Test]
        public void IME確定は許可され_確定文字列と違えば遮断()
        {
            Assert.IsTrue(InputReviewer.Review("", "n", Cand("変換", 0, 0, Ev(EvidenceKind.ImeCommit, "変換")), null).Approved);
            Assert.IsFalse(InputReviewer.Review("", "n", Cand("別の文字", 0, 0, Ev(EvidenceKind.ImeCommit, "変換")), null).Approved);
        }

        [Test]
        public void 証拠のない挿入と外部のドロップは遮断される()
        {
            Assert.IsFalse(InputReviewer.Review("", "n", Cand("x", 0, 0, InputEvidence.None), null).Approved);
            Assert.IsFalse(InputReviewer.Review("", "n", Cand("x", 0, 0, Ev(EvidenceKind.Drop)), null).Approved);
        }

        [Test]
        public void 外部のテキストの貼り付けは遮断され_内部バッファと完全一致する時だけ許可される()
        {
            var clip = new InternalClipboard();
            var blocked = InputReviewer.Review("ab", "n", Cand("ab外部", 2, 2, Ev(EvidenceKind.Paste)), clip);
            Assert.IsFalse(blocked.Approved, "バッファが空");

            clip.Set("n", 0, "内部", LabeledSlice.Uniform(2, Origin.Typed));
            Assert.IsFalse(InputReviewer.Review("ab", "n", Cand("ab外部", 2, 2, Ev(EvidenceKind.Paste)), clip).Approved, "一致しない");
            Assert.IsFalse(InputReviewer.Review("ab", "n", Cand("ab内", 2, 2, Ev(EvidenceKind.Paste)), clip).Approved, "一部だけ一致");

            var ok = InputReviewer.Review("ab", "n", Cand("ab内部", 2, 2, Ev(EvidenceKind.Paste)), clip);
            Assert.IsTrue(ok.Approved);
            Assert.AreEqual(EventSource.Internal, ok.Ops[0].Src);
            Assert.IsTrue(ok.Ops[0].Labels.IsUniform(Origin.Typed));
        }

        [Test]
        public void 別のノートから貼り付けると元のラベルと貼り付け元が残る()
        {
            var clip = new InternalClipboard();
            var labels = LabeledSlice.FromRuns(new[] { new Run(2, Origin.Typed), new Run(1, Origin.Unknown) });
            clip.Set("元ノート", 5, "abc", labels);

            var r = InputReviewer.Review("", "貼り先", Cand("abc", 0, 0, Ev(EvidenceKind.Paste)), clip);
            Assert.IsTrue(r.Approved);
            Assert.AreEqual("T2U1", r.Ops[0].Labels.Encode());
            Assert.AreEqual("元ノート", r.Ops[0].SrcDoc);
            Assert.AreEqual(5, r.Ops[0].From);

            var same = InputReviewer.Review("", "元ノート", Cand("abc", 0, 0, Ev(EvidenceKind.Paste)), clip);
            Assert.IsNull(same.Ops[0].SrcDoc, "同じノートなら貼り付け元は書かない");
        }

        [Test]
        public void 元に戻すは許可され_元のラベルを復元する()
        {
            var labels = LabeledSlice.FromRuns(new[] { new Run(1, Origin.Unknown), new Run(1, Origin.Typed) });
            var ev = new InputEvidence { Kind = EvidenceKind.Undo, Text = "xy", UndoLabels = labels };
            var r = InputReviewer.Review("", "n", Cand("xy", 0, 0, ev), null);
            Assert.IsTrue(r.Approved);
            Assert.AreEqual(EventSource.Undo, r.Ops[0].Src);
            Assert.AreEqual("U1T1", r.Ops[0].Labels.Encode());

            var wrong = new InputEvidence { Kind = EvidenceKind.Undo, Text = "xy" };
            Assert.IsFalse(InputReviewer.Review("", "n", Cand("外部", 0, 0, wrong), null).Approved, "UI が再挿入するはずの文字と違う");

            var noLabels = InputReviewer.Review("", "n", Cand("xy", 0, 0, Ev(EvidenceKind.Undo)), null);
            Assert.IsTrue(noLabels.Approved);
            Assert.IsTrue(noLabels.Ops[0].Labels.IsUniform(Origin.Unknown), "ラベルが分からない再挿入は unknown");
        }

        [Test]
        public void 書式記号と未確認の編集はunknownになる()
        {
            var f = InputReviewer.Review("a", "n", Cand("**a", 0, 0, Ev(EvidenceKind.Format)), null);
            Assert.IsTrue(f.Approved);
            Assert.AreEqual(EventSource.Format, f.Ops[0].Src);
            Assert.IsTrue(f.Ops[0].Labels.IsUniform(Origin.Unknown));

            var u = InputReviewer.Review("a", "n", Cand("abc", 1, 1, Ev(EvidenceKind.Unreviewed)), null);
            Assert.IsTrue(u.Approved);
            Assert.IsTrue(u.Ops[0].Labels.IsUniform(Origin.Unknown));
        }

        [Test]
        public void 削除だけの変更は常に許可される()
        {
            var r = InputReviewer.Review("abc", "n", Cand("ac", 2, 2, InputEvidence.None), null);
            Assert.IsTrue(r.Approved);
            Assert.AreEqual(EventType.Delete, r.Ops.Single().Type);
            Assert.AreEqual(1, r.Ops[0].Pos);
            Assert.AreEqual(1, r.Ops[0].Len);
        }

        [Test]
        public void 置換は削除と挿入の2操作になり_遮断時は削除も行わない()
        {
            var ok = InputReviewer.Review("abcd", "n", Cand("aXd", 1, 3, Ev(EvidenceKind.Key, "X")), null);
            Assert.IsTrue(ok.Approved);
            Assert.AreEqual(new[] { EventType.Delete, EventType.Insert }, ok.Ops.Select(o => o.Type).ToArray());

            var blocked = InputReviewer.Review("abcd", "n", Cand("a外部d", 1, 3, Ev(EvidenceKind.Key, "X")), null);
            Assert.IsFalse(blocked.Approved);
            Assert.AreEqual("abcd", blocked.RevertText);
            Assert.AreEqual(1, blocked.RevertSelStart);
            Assert.AreEqual(3, blocked.RevertSelEnd);
            Assert.AreEqual(EventType.Blocked, blocked.Ops.Single().Type, "削除は記録しない");
        }

        [Test]
        public void 変更がなければ何も起きない()
        {
            var r = InputReviewer.Review("abc", "n", Cand("abc", 1, 1, InputEvidence.None), null);
            Assert.IsTrue(r.Approved);
            Assert.AreEqual(0, r.Ops.Count);
        }

        [Test]
        public void 移動は結果が一致する時だけ許可される()
        {
            var move = new InputEvidence { Kind = EvidenceKind.Move, MoveFrom = 0, MoveLength = 2, MoveTo = 2 };
            var ok = InputReviewer.Review("abcd", "n", Cand("cdab", -1, -1, move), null);
            Assert.IsTrue(ok.Approved);
            Assert.AreEqual(EventType.Move, ok.Ops.Single().Type);

            var bad = InputReviewer.Review("abcd", "n", Cand("cdXY", -1, -1, move), null);
            Assert.IsFalse(bad.Approved, "移動では説明できない内容");
        }

        [Test]
        public void 拒否時の選択範囲は本文の長さに収まる()
        {
            var r = InputReviewer.Review("ab", "n", Cand("abX", 99, 120, InputEvidence.None), null);
            Assert.IsFalse(r.Approved);
            Assert.AreEqual(2, r.RevertSelStart);
            Assert.AreEqual(2, r.RevertSelEnd);
        }
    }
}
