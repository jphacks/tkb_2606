using System;
using System.Collections.Generic;

namespace Cartograph.Core
{
    public enum EvidenceKind
    {
        /// <summary>証拠なし。挿入があれば説明がつかないものとして遮断する。</summary>
        None,
        /// <summary>キー入力。Text に打った 1 文字。</summary>
        Key,
        /// <summary>IME の変換確定。Text に確定した文字列。</summary>
        ImeCommit,
        /// <summary>貼り付け(Ctrl/Cmd+V、右クリック)。</summary>
        Paste,
        /// <summary>ドラッグ&ドロップ。外部からは常に遮断する。</summary>
        Drop,
        Undo,
        Redo,
        /// <summary>書式ボタンが挿入した記号。来歴は unknown。</summary>
        Format,
        /// <summary>ノート内の移動。MoveFrom/MoveLength/MoveTo で指定する。</summary>
        Move,
        /// <summary>
        /// 入力の証拠を確かめずに受け入れる(証拠の経路がまだない呼び出し元の暫定用)。
        /// 来歴は unknown になるので、本文量(typed)には数えられない。
        /// </summary>
        Unreviewed,
    }

    /// <summary>UI が変更に添える「何が起きたか」の証拠。</summary>
    public sealed class InputEvidence
    {
        public static readonly InputEvidence None = new InputEvidence { Kind = EvidenceKind.None };

        public EvidenceKind Kind;
        /// <summary>Key/ImeCommit: 入力された文字列。Undo/Redo: UI が再挿入するはずの文字列(分かれば)。</summary>
        public string Text;
        /// <summary>Undo/Redo で文字を再挿入する時の元のラベル。なければ unknown になる。</summary>
        public LabeledSlice UndoLabels;
        public int MoveFrom, MoveLength, MoveTo;   // MoveTo は元の範囲を取り除いた後の本文での位置
    }

    /// <summary>テキスト欄の値が変わった時に UI が渡す変更の候補。変更前の本文は NoteSession が持っている。</summary>
    public sealed class ChangeCandidate
    {
        public string NewText;
        /// <summary>変更前の選択範囲(カーソルだけなら同じ値)。不明なら -1。</summary>
        public int SelStartBefore = -1;
        public int SelEndBefore = -1;
        public InputEvidence Evidence = InputEvidence.None;
    }

    /// <summary>審査の結果、本文に適用する 1 つの操作。</summary>
    public sealed class PlannedOp
    {
        public string Type;
        public int Pos;
        public int Len;
        public string Text;
        public string Src;
        public int From = -1;
        public string SrcDoc;
        public LabeledSlice Labels;
    }

    public sealed class ReviewResult
    {
        public bool Approved;
        /// <summary>拒否した時に UI が本文へ戻す内容と選択範囲。</summary>
        public string RevertText;
        public int RevertSelStart, RevertSelEnd;
        /// <summary>拒否した理由。UI の上部に短く表示する。</summary>
        public string Reason;
        public IReadOnlyList<PlannedOp> Ops;
    }

    /// <summary>アプリ内のコピー・切り取りで預かった内容。ノート間の貼り付けでも元のラベルを引き継ぐ。</summary>
    public sealed class InternalClipboard
    {
        public string SourceNote { get; private set; }
        public int SourcePos { get; private set; } = -1;
        public string Text { get; private set; }
        public LabeledSlice Labels { get; private set; }
        public bool HasContent => !string.IsNullOrEmpty(Text);

        public void Set(string sourceNote, int sourcePos, string text, LabeledSlice labels)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (labels == null || labels.Length != text.Length)
                throw new ArgumentException("ラベルの長さが文字列と一致しません。", nameof(labels));
            SourceNote = sourceNote;
            SourcePos = sourcePos;
            Text = text;
            Labels = labels;
        }

        internal void RenameSource(string from, string to)
        {
            if (SourceNote == from) SourceNote = to;
        }

        public void Clear()
        {
            SourceNote = null;
            SourcePos = -1;
            Text = null;
            Labels = null;
        }
    }

    /// <summary>
    /// 変更後の差分を見て、説明のつく挿入だけを許可する。経路ごとに止めるのではなく、結果で判定する。
    /// 説明できる挿入: キー入力の 1 文字 / IME 確定 / 内部バッファと完全一致 / 元に戻す・やり直し。
    /// </summary>
    public static class InputReviewer
    {
        public const string BlockedMessage = "外部からの貼り付けはできません";

        public static ReviewResult Review(string oldText, string noteValue, ChangeCandidate c, InternalClipboard clipboard)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            oldText = oldText ?? "";
            string newText = c.NewText ?? "";
            var ev = c.Evidence ?? InputEvidence.None;

            if (ev.Kind == EvidenceKind.Move) return ReviewMove(oldText, newText, c, ev);

            var d = TextDiff.Compute(oldText, newText, c.SelStartBefore, c.SelEndBefore);
            if (d.IsEmpty) return Approve(new List<PlannedOp>());

            string src = SourceOf(ev.Kind);
            var ops = new List<PlannedOp>();
            if (d.Deleted.Length > 0)
                ops.Add(new PlannedOp { Type = EventType.Delete, Pos = d.Pos, Len = d.Deleted.Length, Src = src });
            if (d.Inserted.Length == 0) return Approve(ops);

            var ins = new PlannedOp { Type = EventType.Insert, Pos = d.Pos, Text = d.Inserted, Src = src };
            if (!Explain(ev, d.Inserted, noteValue, clipboard, ins)) return Reject(oldText, c, d, src);
            ops.Add(ins);
            return Approve(ops);
        }

        // 挿入 inserted が証拠で説明できれば、ins のラベルなどを埋めて true を返す。
        private static bool Explain(InputEvidence ev, string inserted, string noteValue, InternalClipboard clipboard, PlannedOp ins)
        {
            switch (ev.Kind)
            {
                case EvidenceKind.Key:
                    if (ev.Text == null || ev.Text != inserted || !IsSingleCodePoint(inserted)) return false;
                    ins.Labels = LabeledSlice.Uniform(inserted.Length, Origin.Typed);
                    return true;

                case EvidenceKind.ImeCommit:
                    if (ev.Text == null || ev.Text != inserted) return false;
                    ins.Labels = LabeledSlice.Uniform(inserted.Length, Origin.Typed);
                    return true;

                case EvidenceKind.Paste:
                    if (clipboard == null || !clipboard.HasContent || clipboard.Text != inserted) return false;
                    ins.Labels = clipboard.Labels;
                    ins.From = clipboard.SourcePos;
                    ins.SrcDoc = clipboard.SourceNote != noteValue ? clipboard.SourceNote : null;
                    return true;

                case EvidenceKind.Undo:
                case EvidenceKind.Redo:
                    if (ev.Text != null && ev.Text != inserted) return false;
                    ins.Labels = ev.UndoLabels != null && ev.UndoLabels.Length == inserted.Length
                        ? ev.UndoLabels
                        : LabeledSlice.Uniform(inserted.Length, Origin.Unknown);
                    return true;

                case EvidenceKind.Format:
                case EvidenceKind.Unreviewed:
                    ins.Labels = LabeledSlice.Uniform(inserted.Length, Origin.Unknown);
                    return true;

                default:   // None, Drop
                    return false;
            }
        }

        private static ReviewResult ReviewMove(string oldText, string newText, ChangeCandidate c, InputEvidence ev)
        {
            int from = ev.MoveFrom, len = ev.MoveLength, to = ev.MoveTo;
            bool valid = from >= 0 && len > 0 && from + (long)len <= oldText.Length;
            if (valid)
            {
                string moved = oldText.Substring(from, len);
                string rest = oldText.Remove(from, len);
                valid = to >= 0 && to <= rest.Length && rest.Insert(to, moved) == newText;
            }
            if (!valid)
                return Reject(oldText, c, TextDiff.Compute(oldText, newText, c.SelStartBefore, c.SelEndBefore), EventSource.Key);

            return Approve(new List<PlannedOp>
            {
                new PlannedOp { Type = EventType.Move, Pos = to, Len = len, From = from, Src = EventSource.Key },
            });
        }

        private static ReviewResult Approve(List<PlannedOp> ops) => new ReviewResult { Approved = true, Ops = ops };

        private static ReviewResult Reject(string oldText, ChangeCandidate c, DiffResult d, string src)
        {
            int s = c.SelStartBefore < 0 ? oldText.Length : Math.Min(c.SelStartBefore, oldText.Length);
            int e = c.SelEndBefore < 0 ? s : Math.Min(Math.Max(c.SelEndBefore, s), oldText.Length);
            return new ReviewResult
            {
                Approved = false,
                RevertText = oldText,
                RevertSelStart = s,
                RevertSelEnd = e,
                Reason = BlockedMessage,
                // 遮断した本文は残さず、位置と長さだけ記録する
                Ops = new List<PlannedOp>
                {
                    new PlannedOp { Type = EventType.Blocked, Pos = d.Pos, Len = Math.Max(d.Inserted.Length, 1), Src = src },
                },
            };
        }

        private static string SourceOf(EvidenceKind kind)
        {
            switch (kind)
            {
                case EvidenceKind.ImeCommit: return EventSource.Ime;
                case EvidenceKind.Paste: return EventSource.Internal;
                case EvidenceKind.Undo:
                case EvidenceKind.Redo: return EventSource.Undo;
                case EvidenceKind.Format: return EventSource.Format;
                default: return EventSource.Key;
            }
        }

        private static bool IsSingleCodePoint(string s)
        {
            if (s.Length == 1) return !char.IsSurrogate(s[0]);
            return s.Length == 2 && char.IsHighSurrogate(s[0]) && char.IsLowSurrogate(s[1]);
        }
    }
}
