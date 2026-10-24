using System;
using System.Collections.Generic;

namespace Cartograph.Analysis
{
    /// <summary>
    /// 感情語の辞書。語と感情の分類を持ち、本文の文字列から最長一致で語を探す(形態素解析は使わない)。
    /// 語は 2 文字以上のものだけ。い形容詞は活用しても当たるよう、語幹(例: "嬉し")で登録する。
    /// ただし語幹が別の語の一部に当たる(例: "音楽します" の "楽し")ときは、その別の語を「除外語」として登録する。
    /// 除外語は最長一致で先に読み飛ばされ、花にならない。
    ///
    /// CSV の形式(仮): 1 行に「語,分類」。分類は 喜 / 怒 / 哀 / 楽(または joy / anger / sorrow / pleasure)、
    /// 除外語は 除外(または none / exclude)。空行と、先頭が '#' の行は無視する。
    /// </summary>
    public sealed class EmotionLexicon
    {
        /// <summary>語の直後にこれが続くと、否定として枯れた花にする(文字列の一致だけで判定する。仮)。</summary>
        public static readonly IReadOnlyList<string> DefaultNegations = new[]
        {
            "ない", "なかった", "ません", "くない", "くなかった", "くありません",
            "じゃない", "じゃなかった", "ではない", "ではありません",
        };

        /// <summary>語を 1 つも持たない辞書。花は咲かない。</summary>
        public static readonly EmotionLexicon Empty = new EmotionLexicon(new KeyValuePair<string, Emotion>[0]);

        private static EmotionLexicon defaultLexicon;

        /// <summary>初版の辞書(下書き。語は増やして、あとで見直す)。</summary>
        public static EmotionLexicon Default => defaultLexicon ?? (defaultLexicon = FromCsv(DefaultEmotionWords.Csv, out _));

        // 値が null の語は除外語。
        private readonly Dictionary<string, Emotion?> words = new Dictionary<string, Emotion?>(StringComparer.Ordinal);
        private readonly Dictionary<char, string[]> byFirstChar = new Dictionary<char, string[]>();
        private readonly string[] negations;

        /// <summary>感情語の数(除外語は含めない)。</summary>
        public int Count { get; }

        /// <summary>除外語の数。</summary>
        public int ExcludedCount => words.Count - Count;

        public EmotionLexicon(IEnumerable<KeyValuePair<string, Emotion>> entries,
            IEnumerable<string> negations = null, IEnumerable<string> excludedWords = null)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            foreach (var entry in entries)
            {
                CheckWord(entry.Key);
                if (!words.ContainsKey(entry.Key)) words.Add(entry.Key, entry.Value);
            }
            Count = words.Count;
            if (excludedWords != null)
            {
                foreach (var word in excludedWords)
                {
                    CheckWord(word);
                    if (!words.ContainsKey(word)) words.Add(word, null);
                }
            }

            var grouped = new Dictionary<char, List<string>>();
            foreach (var word in words.Keys)
            {
                if (!grouped.TryGetValue(word[0], out var list)) grouped[word[0]] = list = new List<string>();
                list.Add(word);
            }
            foreach (var pair in grouped)
            {
                pair.Value.Sort((a, b) => b.Length.CompareTo(a.Length));     // 長い語を先に試す
                byFirstChar[pair.Key] = pair.Value.ToArray();
            }

            var neg = new List<string>(negations ?? DefaultNegations);
            neg.RemoveAll(string.IsNullOrEmpty);
            this.negations = neg.ToArray();
        }

        private static void CheckWord(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length < 2)
                throw new ArgumentException("辞書の語は 2 文字以上にしてください: " + word);
        }

        /// <summary>CSV から作る。不正な行は飛ばし、理由を errors に入れる。</summary>
        public static EmotionLexicon FromCsv(string csv, out IReadOnlyList<string> errors, IEnumerable<string> negations = null)
        {
            var errorList = new List<string>();
            var entries = new List<KeyValuePair<string, Emotion>>();
            var excluded = new List<string>();
            var seen = new Dictionary<string, Emotion?>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(csv))
            {
                if (csv[0] == '﻿') csv = csv.Substring(1);
                string[] lines = csv.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] cols = line.Split(',');
                    string where = (i + 1) + " 行目: ";
                    if (cols.Length < 2) { errorList.Add(where + "「語,分類」の形式ではありません"); continue; }
                    string word = cols[0].Trim();
                    if (word.Length < 2) { errorList.Add(where + "語は 2 文字以上にしてください"); continue; }

                    string category = cols[1].Trim();
                    Emotion? emotion;
                    if (IsExcludeCategory(category)) emotion = null;
                    else if (TryParseEmotion(category, out var parsed)) emotion = parsed;
                    else { errorList.Add(where + "分類が分かりません: " + category); continue; }

                    if (seen.TryGetValue(word, out var prev))
                    {
                        if (prev != emotion) errorList.Add(where + "「" + word + "」は別の分類で登録済みです(先のものを使います)");
                        continue;
                    }
                    seen.Add(word, emotion);
                    if (emotion.HasValue) entries.Add(new KeyValuePair<string, Emotion>(word, emotion.Value));
                    else excluded.Add(word);
                }
            }
            errors = errorList;
            return new EmotionLexicon(entries, negations, excluded);
        }

        private static bool IsExcludeCategory(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "除外": case "none": case "exclude": return true;
                default: return false;
            }
        }

        public static bool TryParseEmotion(string s, out Emotion emotion)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "喜": case "joy": emotion = Emotion.Joy; return true;
                case "怒": case "anger": emotion = Emotion.Anger; return true;
                case "哀": case "sorrow": emotion = Emotion.Sorrow; return true;
                case "楽": case "pleasure": emotion = Emotion.Pleasure; return true;
                default: emotion = default; return false;
            }
        }

        /// <summary>
        /// text の index から始まる最長の語を探す。end(排他)をまたぐ語は対象にしない。
        /// 見つかれば true。除外語のときは emotion が null(読み飛ばして、花にはしない)。
        /// </summary>
        public bool TryMatchAt(string text, int index, int end, out string word, out Emotion? emotion)
        {
            word = null;
            emotion = null;
            if (index < 0 || index >= end || !byFirstChar.TryGetValue(text[index], out var candidates)) return false;
            foreach (var candidate in candidates)
            {
                if (candidate.Length > end - index) continue;
                if (string.CompareOrdinal(text, index, candidate, 0, candidate.Length) != 0) continue;
                word = candidate;
                emotion = words[candidate];
                return true;
            }
            return false;
        }

        /// <summary>after(語の直後)から end(排他)までのうちに、否定の言い回しが続いているか。</summary>
        public bool IsNegated(string text, int after, int end)
        {
            foreach (var n in negations)
            {
                if (n.Length <= end - after && string.CompareOrdinal(text, after, n, 0, n.Length) == 0) return true;
            }
            return false;
        }
    }
}
