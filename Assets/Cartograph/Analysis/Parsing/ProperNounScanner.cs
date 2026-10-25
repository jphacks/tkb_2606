using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cartograph.Core;

namespace Cartograph.Analysis
{
    /// <summary>
    /// 固有名詞ノートの別名を読み、本文から別名を探して建物にする。
    /// 別名の書式: ノートの先頭行「別名: 田中, たなか」。区切りは , ， 、、コロンは全角も可。
    /// 検出は左からの最長一致。照合では全角英数を半角に、英字を小文字にそろえる(文字数は変えない)。
    /// 1 文字の別名は、前後どちらかが漢字なら検出しない(「田」が「田中」の中で当たらないように)。
    /// 数えるのは本文のみ。コードブロック、固有名詞ノート自身、インラインコードは数えない。
    /// </summary>
    public static class ProperNounScanner
    {
        private const char Skipped = '\0';

        private static readonly Regex AliasLineRegex = new Regex(@"^[ \t]*別名[ \t]*[:：](.*)$", RegexOptions.Compiled);
        private static readonly char[] AliasSeparators = { ',', '，', '、', '､' };

        /// <summary>先頭行から別名を取り出す。「別名:」行でない、または別名が 1 つもなければ空。重複は除く。</summary>
        public static List<string> ParseAliases(string firstLine)
        {
            var aliases = new List<string>();
            if (firstLine == null) return aliases;
            var m = AliasLineRegex.Match(firstLine);
            if (!m.Success) return aliases;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in m.Groups[1].Value.Split(AliasSeparators))
            {
                string alias = part.Trim(' ', '\t', '　');
                if (alias.Length > 0 && seen.Add(Normalize(alias))) aliases.Add(alias);
            }
            return aliases;
        }

        /// <summary>1 行の本文を走査用に変える。見出しなら全体を Strong、太字は Medium、それ以外は Weak。</summary>
        public static ScanLine BuildScanLine(string node, int lineNumber, string body, bool heading)
        {
            var chars = body.ToCharArray();
            var levels = new byte[chars.Length];
            bool bold = false;
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '`')
                {
                    int close = body.IndexOf('`', i + 1);
                    if (close > i)
                    {
                        for (int k = i; k <= close; k++) chars[k] = Skipped;
                        i = close;
                        continue;
                    }
                }
                if ((c == '*' || c == '_') && i + 1 < chars.Length && chars[i + 1] == c)
                {
                    if (bold || body.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal) >= 0)
                    {
                        bold = !bold;
                        chars[i] = chars[i + 1] = Skipped;
                        i++;
                        continue;
                    }
                }
                if ((c == '[' || c == ']') && i + 1 < chars.Length && chars[i + 1] == c)
                {
                    chars[i] = chars[i + 1] = Skipped;
                    i++;
                    continue;
                }
                levels[i] = (byte)(heading ? Emphasis.Strong : bold ? Emphasis.Medium : Emphasis.Weak);
            }
            return new ScanLine(node, lineNumber, new string(chars), levels);
        }

        /// <summary>全ノートの別名と走査行から、建物を作る。出現した順(ノート順、行順)に並ぶ。</summary>
        public static List<Building> FindBuildings(IReadOnlyList<NoteAnalysis> notes)
        {
            // 別名 → 持ち主のノート。複数のノートが同じ別名を宣言していれば、曖昧なので使わない。
            var owners = new Dictionary<string, int>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < notes.Count; i++)
            {
                foreach (string alias in notes[i].Aliases)
                {
                    string key = Normalize(alias);
                    if (owners.TryGetValue(key, out int owner))
                    {
                        if (owner != i) ambiguous.Add(key);
                    }
                    else owners[key] = i;
                }
            }
            foreach (string key in ambiguous) owners.Remove(key);
            if (owners.Count == 0) return new List<Building>();

            var byFirstChar = new Dictionary<char, List<string>>();
            foreach (string key in owners.Keys)
            {
                if (!byFirstChar.TryGetValue(key[0], out var list)) byFirstChar[key[0]] = list = new List<string>();
                list.Add(key);
            }
            foreach (var list in byFirstChar.Values) list.Sort((a, b) => b.Length.CompareTo(a.Length));

            var order = new List<(int Owner, string Node)>();
            var counts = new Dictionary<(int, string), int[]>();     // [Strong, Medium, Weak]
            for (int n = 0; n < notes.Count; n++)
            {
                foreach (var line in notes[n].ScanLines)
                {
                    string text = Normalize(line.Text);
                    int i = 0;
                    while (i < text.Length)
                    {
                        string hit = null;
                        if (byFirstChar.TryGetValue(text[i], out var candidates))
                        {
                            foreach (string alias in candidates)
                            {
                                if (string.CompareOrdinal(text, i, alias, 0, alias.Length) != 0) continue;
                                if (alias.Length == 1 && (IsKanjiAt(line.Text, i - 1) || IsKanjiAt(line.Text, i + 1))) continue;
                                hit = alias;
                                break;
                            }
                        }
                        if (hit == null) { i++; continue; }

                        int owner = owners[hit];
                        if (owner != n)
                        {
                            byte level = 0;
                            for (int k = i; k < i + hit.Length; k++) if (line.Levels[k] > level) level = line.Levels[k];
                            var key = (owner, line.Node);
                            if (!counts.TryGetValue(key, out var c))
                            {
                                counts[key] = c = new int[3];
                                order.Add(key);
                            }
                            c[level == (byte)Emphasis.Strong ? 0 : level == (byte)Emphasis.Medium ? 1 : 2]++;
                        }
                        i += hit.Length;
                    }
                }
            }

            var buildings = new List<Building>(order.Count);
            foreach (var key in order)
            {
                var c = counts[key];
                buildings.Add(new Building(notes[key.Owner].NoteId, key.Node, c[0], c[1], c[2]));
            }
            return buildings;
        }

        /// <summary>照合用にそろえる。文字数は変えない。</summary>
        public static string Normalize(string text)
        {
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);
                chars[i] = char.ToLowerInvariant(c);
            }
            return new string(chars);
        }

        private static bool IsKanjiAt(string text, int index)
        {
            if (index < 0 || index >= text.Length) return false;
            char c = text[index];
            return (c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || (c >= '豈' && c <= '﫿') || c == '々';
        }
    }
}
