using System;
using System.Collections.Generic;

namespace Cartograph.Core
{
    /// <summary>
    /// 本文と、同じ長さの来歴ラベル列。位置と長さはすべて UTF-16 単位(C# の string と同じ)。
    /// </summary>
    public sealed class LabeledText
    {
        private string text;
        private readonly List<Run> runs;

        public LabeledText() : this("", Origin.Unknown) { }

        private LabeledText(string text, List<Run> runs)
        {
            this.text = text;
            this.runs = runs;
        }

        public LabeledText(string text, Origin origin)
        {
            this.text = text ?? "";
            runs = new List<Run>();
            if (this.text.Length > 0) runs.Add(new Run(this.text.Length, origin));
        }

        public string Text => text;
        public int Length => text.Length;
        public LabeledSlice Labels => new LabeledSlice(runs);

        public static bool TryFromEncoded(string text, string lbl, out LabeledText result)
        {
            result = null;
            text = text ?? "";
            if (!LabeledSlice.TryDecode(lbl ?? "", text.Length, out var labels)) return false;
            result = new LabeledText(text, new List<Run>(labels.Runs));
            return true;
        }

        public LabeledText Clone() => new LabeledText(text, new List<Run>(runs));

        public string EncodeLabels() => Labels.Encode();

        public LabeledSlice Slice(int pos, int len)
        {
            CheckRange(pos, len);
            var result = new List<Run>();
            int acc = 0;
            int end = pos + len;
            foreach (var r in runs)
            {
                int rs = acc, re = acc + r.Length;
                acc = re;
                int s = Math.Max(rs, pos), e = Math.Min(re, end);
                if (e > s) result.Add(new Run(e - s, r.Origin));
                if (re >= end) break;
            }
            return new LabeledSlice(result);
        }

        public void Insert(int pos, string s, LabeledSlice labels)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (labels == null) throw new ArgumentNullException(nameof(labels));
            if (pos < 0 || pos > text.Length) throw new ArgumentOutOfRangeException(nameof(pos));
            if (labels.Length != s.Length) throw new ArgumentException("ラベルの長さが文字列と一致しません。", nameof(labels));
            if (s.Length == 0) return;

            int idx = SplitAt(pos);
            runs.InsertRange(idx, labels.Runs);
            text = text.Insert(pos, s);
            Normalize();
        }

        /// <summary>範囲を削除し、消したラベル列を返す(元に戻す時の復元用)。</summary>
        public LabeledSlice Delete(int pos, int len)
        {
            CheckRange(pos, len);
            if (len == 0) return LabeledSlice.Empty;

            int i = SplitAt(pos);
            int j = SplitAt(pos + len);
            var removed = runs.GetRange(i, j - i);
            runs.RemoveRange(i, j - i);
            text = text.Remove(pos, len);
            Normalize();
            return new LabeledSlice(removed);
        }

        /// <summary>範囲内の typed 文字数。空白と改行は数えない。</summary>
        public int TypedCount(int pos, int len)
        {
            CheckRange(pos, len);
            int count = 0;
            int acc = 0;
            int end = pos + len;
            foreach (var r in runs)
            {
                int rs = acc, re = acc + r.Length;
                acc = re;
                if (r.Origin == Origin.Typed)
                {
                    int s = Math.Max(rs, pos), e = Math.Min(re, end);
                    for (int k = s; k < e; k++)
                        if (!char.IsWhiteSpace(text[k])) count++;
                }
                if (re >= end) break;
            }
            return count;
        }

        public int TypedCount() => TypedCount(0, text.Length);

        private void CheckRange(int pos, int len)
        {
            if (pos < 0 || len < 0 || pos > text.Length || len > text.Length - pos)
                throw new ArgumentOutOfRangeException(nameof(pos), "範囲が本文の外にあります。");
        }

        // pos の位置に区間の境界を作り、その境界の runs 内の位置を返す。
        private int SplitAt(int pos)
        {
            int acc = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                if (pos == acc) return i;
                int end = acc + runs[i].Length;
                if (pos < end)
                {
                    var r = runs[i];
                    runs[i] = new Run(pos - acc, r.Origin);
                    runs.Insert(i + 1, new Run(end - pos, r.Origin));
                    return i + 1;
                }
                acc = end;
            }
            return runs.Count;
        }

        private void Normalize()
        {
            var merged = new List<Run>(runs.Count);
            foreach (var r in runs)
            {
                if (r.Length <= 0) continue;
                int last = merged.Count - 1;
                if (last >= 0 && merged[last].Origin == r.Origin)
                    merged[last] = new Run(merged[last].Length + r.Length, r.Origin);
                else
                    merged.Add(r);
            }
            runs.Clear();
            runs.AddRange(merged);
        }
    }
}
