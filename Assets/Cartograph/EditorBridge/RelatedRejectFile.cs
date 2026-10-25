using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cartograph.Analysis;

namespace Cartograph.EditorUI
{
    /// <summary>却下した関連ノートの組の保存形式(.cartograph/related-rejected.txt)。1 行に「ノートID TAB ノートID」。</summary>
    public static class RelatedRejectFile
    {
        /// <summary>壊れた行は読み飛ばす。ファイルがなくても、壊れていても、空の集合として読む。</summary>
        public static HashSet<RelatedPair> Parse(string text)
        {
            var pairs = new HashSet<RelatedPair>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var parts = raw.TrimEnd('\r').Split('\t');
                if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 && parts[0] != parts[1])
                    pairs.Add(new RelatedPair(parts[0], parts[1]));
            }
            return pairs;
        }

        public static string Serialize(IEnumerable<RelatedPair> pairs)
        {
            var text = new StringBuilder();
            foreach (var pair in (pairs ?? Enumerable.Empty<RelatedPair>())
                .OrderBy(p => p.A, StringComparer.Ordinal).ThenBy(p => p.B, StringComparer.Ordinal))
                text.Append(pair.A).Append('\t').Append(pair.B).Append('\n');
            return text.ToString();
        }
    }
}
