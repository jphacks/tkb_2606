using System;
using System.Collections.Generic;

namespace Cartograph.Core
{
    public sealed class ReplayException : Exception
    {
        public ReplayException(string message) : base(message) { }
    }

    /// <summary>
    /// 履歴イベントを本文とラベル列に適用する。NoteSession の編集もこの 1 経路を通すので、
    /// 履歴を再生した結果は、編集した結果と必ず一致する。
    /// </summary>
    public static class ProvenanceReplayer
    {
        public static void Replay(LabeledText doc, IEnumerable<EditEvent> events)
        {
            foreach (var e in events) Apply(doc, e);
        }

        public static void Apply(LabeledText doc, EditEvent e)
        {
            switch (e.Type)
            {
                case EventType.Blocked:
                    return;

                case EventType.Insert:
                {
                    if (e.Text == null) throw new ReplayException("insert に text がありません。");
                    if (e.Pos < 0 || e.Pos > doc.Length) throw new ReplayException("insert の位置が範囲外です。");
                    doc.Insert(e.Pos, e.Text, LabelsOf(doc, e));
                    return;
                }

                case EventType.Delete:
                {
                    if (e.Pos < 0 || e.Len < 0 || e.Pos + (long)e.Len > doc.Length)
                        throw new ReplayException("delete の範囲が範囲外です。");
                    doc.Delete(e.Pos, e.Len);
                    return;
                }

                case EventType.Move:
                {
                    if (e.From < 0 || e.Len < 0 || e.From + (long)e.Len > doc.Length)
                        throw new ReplayException("move の元の範囲が範囲外です。");
                    string moved = doc.Text.Substring(e.From, e.Len);
                    var labels = doc.Delete(e.From, e.Len);
                    if (e.Pos < 0 || e.Pos > doc.Length) throw new ReplayException("move の挿入位置が範囲外です。");
                    doc.Insert(e.Pos, moved, labels);
                    return;
                }

                default:
                    throw new ReplayException("未知のイベント種別です: " + e.Type);
            }
        }

        private static LabeledSlice LabelsOf(LabeledText doc, EditEvent e)
        {
            int len = e.Text.Length;
            if (e.Lbl != null)
            {
                if (!LabeledSlice.TryDecode(e.Lbl, len, out var decoded))
                    throw new ReplayException("lbl の形式または長さが不正です。");
                return decoded;
            }

            if (e.Src == EventSource.Key || e.Src == EventSource.Ime)
                return LabeledSlice.Uniform(len, Origin.Typed);

            // lbl のない internal(仕様書どおりのログ): 同じノートの from の文字から引き継ぐ。
            if (e.Src == EventSource.Internal && e.From >= 0 && string.IsNullOrEmpty(e.SrcDoc)
                && e.From + (long)len <= doc.Length
                && string.CompareOrdinal(doc.Text, e.From, e.Text, 0, len) == 0)
            {
                return doc.Slice(e.From, len);
            }

            return LabeledSlice.Uniform(len, Origin.Unknown);
        }
    }
}
