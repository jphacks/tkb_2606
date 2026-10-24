using System;
using System.Collections.Generic;
using System.Text;

namespace Cartograph.Core
{
    /// <summary>文字の来歴。typed = 自分で打った、unknown = 来歴不明。</summary>
    public enum Origin : byte { Unknown = 0, Typed = 1 }

    /// <summary>同じ来歴が続く区間(連長)。</summary>
    public readonly struct Run
    {
        public readonly int Length;
        public readonly Origin Origin;
        public Run(int length, Origin origin) { Length = length; Origin = origin; }
    }

    /// <summary>
    /// 文字列の一部に対する来歴ラベル列。不変。
    /// 履歴ログの lbl は "T3U2T5" のように Encode したもの(T = typed, U = unknown, 数字 = 長さ)。
    /// </summary>
    public sealed class LabeledSlice
    {
        public static readonly LabeledSlice Empty = new LabeledSlice(new List<Run>());

        private readonly Run[] runs;

        public int Length { get; }
        public IReadOnlyList<Run> Runs => runs;

        internal LabeledSlice(List<Run> source)
        {
            var merged = new List<Run>(source.Count);
            foreach (var r in source)
            {
                if (r.Length <= 0) continue;
                int last = merged.Count - 1;
                if (last >= 0 && merged[last].Origin == r.Origin)
                    merged[last] = new Run(merged[last].Length + r.Length, r.Origin);
                else
                    merged.Add(r);
            }
            runs = merged.ToArray();
            int total = 0;
            foreach (var r in runs) total += r.Length;
            Length = total;
        }

        public static LabeledSlice Uniform(int length, Origin origin)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            return new LabeledSlice(new List<Run> { new Run(length, origin) });
        }

        public bool IsUniform(Origin origin)
        {
            foreach (var r in runs) if (r.Origin != origin) return false;
            return true;
        }

        public Origin OriginAt(int index)
        {
            if (index < 0 || index >= Length) throw new ArgumentOutOfRangeException(nameof(index));
            int acc = 0;
            foreach (var r in runs)
            {
                acc += r.Length;
                if (index < acc) return r.Origin;
            }
            throw new InvalidOperationException();
        }

        public string Encode()
        {
            var sb = new StringBuilder();
            foreach (var r in runs)
            {
                sb.Append(r.Origin == Origin.Typed ? 'T' : 'U');
                sb.Append(r.Length);
            }
            return sb.ToString();
        }

        /// <summary>lbl を復元する。形式が不正、または合計が expectedLength と合わなければ false。</summary>
        public static bool TryDecode(string lbl, int expectedLength, out LabeledSlice result)
        {
            result = null;
            if (lbl == null || expectedLength < 0) return false;
            var list = new List<Run>();
            long total = 0;
            int i = 0;
            while (i < lbl.Length)
            {
                char c = lbl[i++];
                Origin origin;
                if (c == 'T') origin = Origin.Typed;
                else if (c == 'U') origin = Origin.Unknown;
                else return false;

                int start = i;
                long len = 0;
                while (i < lbl.Length && lbl[i] >= '0' && lbl[i] <= '9')
                {
                    len = len * 10 + (lbl[i] - '0');
                    if (len > int.MaxValue) return false;
                    i++;
                }
                if (i == start || len == 0) return false;
                total += len;
                if (total > expectedLength) return false;
                list.Add(new Run((int)len, origin));
            }
            if (total != expectedLength) return false;
            result = new LabeledSlice(list);
            return true;
        }
    }
}
