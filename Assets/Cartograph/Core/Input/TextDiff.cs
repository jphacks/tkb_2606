using System;

namespace Cartograph.Core
{
    public readonly struct DiffResult
    {
        public readonly int Pos;
        public readonly string Deleted;
        public readonly string Inserted;

        public DiffResult(int pos, string deleted, string inserted)
        {
            Pos = pos;
            Deleted = deleted ?? "";
            Inserted = inserted ?? "";
        }

        public bool IsEmpty => Deleted.Length == 0 && Inserted.Length == 0;
    }

    /// <summary>変更前後の本文から、変わった部分(削除された文字列・挿入された文字列)を取り出す。</summary>
    public static class TextDiff
    {
        /// <summary>
        /// selStart/selEnd は変更前の選択範囲(カーソルだけなら同じ値)。分かれば、同じ文字が続く時の位置を
        /// 選択範囲に合わせる。使えない値(負数など)なら先頭・末尾の一致だけで求める。
        /// </summary>
        public static DiffResult Compute(string oldText, string newText, int selStart = -1, int selEnd = -1)
        {
            oldText = oldText ?? "";
            newText = newText ?? "";

            if (TryAnchored(oldText, newText, selStart, selEnd, out var anchored)) return anchored;
            return Minimal(oldText, newText);
        }

        private static bool TryAnchored(string oldText, string newText, int selStart, int selEnd, out DiffResult result)
        {
            result = default;
            if (selStart < 0 || selEnd < selStart || selEnd > oldText.Length) return false;

            int tail = oldText.Length - selEnd;
            int insertedLen = newText.Length - selStart - tail;
            if (insertedLen < 0) return false;
            if (string.CompareOrdinal(oldText, 0, newText, 0, selStart) != 0) return false;
            if (string.CompareOrdinal(oldText, selEnd, newText, newText.Length - tail, tail) != 0) return false;

            string deleted = oldText.Substring(selStart, selEnd - selStart);
            string inserted = newText.Substring(selStart, insertedLen);
            if (deleted == inserted)
            {
                result = new DiffResult(selStart, "", "");   // 同じ文字を打ち直しただけ
                return true;
            }
            result = new DiffResult(selStart, deleted, inserted);
            return true;
        }

        private static DiffResult Minimal(string oldText, string newText)
        {
            int prefix = 0;
            int limit = Math.Min(oldText.Length, newText.Length);
            while (prefix < limit && oldText[prefix] == newText[prefix]) prefix++;
            // サロゲートペアの途中で切らない
            if (prefix > 0 && char.IsHighSurrogate(oldText[prefix - 1])) prefix--;

            int suffix = 0;
            int suffixLimit = limit - prefix;
            while (suffix < suffixLimit &&
                   oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
                suffix++;
            if (suffix > 0 && char.IsLowSurrogate(oldText[oldText.Length - suffix])) suffix--;

            return new DiffResult(
                prefix,
                oldText.Substring(prefix, oldText.Length - prefix - suffix),
                newText.Substring(prefix, newText.Length - prefix - suffix));
        }
    }
}
