using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Cartograph.Analysis
{
    /// <summary>
    /// ノート同士の関連を、本文から自動で見つける(候補を返すだけで、リンクは作らない)。
    /// 根拠は 4 つ。点は <see cref="RelatedNoteOptions"/> で調整する。
    ///  - 言及: 一方の本文にもう一方のノート名(題名・別名・ファイル名)が出てくる
    ///  - 固有名詞: 別名つきのノート(固有名詞ノート)に、両方の本文が触れている
    ///  - キーワード: 両方に出る語(漢字 2 文字以上・カタカナ 2 文字以上・英数 3 文字以上の連なり)。
    ///    ありふれた語(多くのノートに出る語)は外す
    ///  - 感情: <see cref="EmotionLexicon"/> で数えた感情語の分布が近い(補助。既定では単独で候補にならない)
    /// 本文のコードブロック、インラインコード、先頭の「別名:」行は根拠に使わない。
    /// 除外の組(確定済みのリンク・却下済み)は候補に出さない。
    /// 差分更新: <see cref="DetectFor"/> は 1 ノートに関わる組だけを再計算する。ありふれた語の判定には
    /// 全ノートを使うので、他のノートに対する結果とずれることはほぼないが、別名の編集・名前変更のときは
    /// <see cref="Detect"/> で全件を計算し直すこと。
    /// </summary>
    public sealed class RelatedNoteDetector
    {
        private readonly RelatedNoteOptions options;
        private readonly EmotionLexicon lexicon;

        /// <param name="options">null なら既定値。</param>
        /// <param name="lexicon">null なら感情の類似は見ない。</param>
        public RelatedNoteDetector(RelatedNoteOptions options = null, EmotionLexicon lexicon = null)
        {
            this.options = options ?? new RelatedNoteOptions();
            this.lexicon = lexicon;
        }

        /// <summary>全ノートの組を調べ、合計点の高い順(同点は ID 順)に候補を返す。</summary>
        public IReadOnlyList<RelatedNoteCandidate> Detect(IEnumerable<RelatedNoteSource> notes, IEnumerable<RelatedPair> excluded = null)
        {
            var context = new Context(options, lexicon, notes);
            var skip = ToSet(excluded);
            var result = new List<RelatedNoteCandidate>();
            for (int i = 0; i < context.Profiles.Count; i++)
                for (int j = i + 1; j < context.Profiles.Count; j++)
                    AddIfRelated(result, context, context.Profiles[i], context.Profiles[j], skip);
            Sort(result);
            return result;
        }

        /// <summary>noteId に関わる組だけを調べる。ノートが見つからなければ空。</summary>
        public IReadOnlyList<RelatedNoteCandidate> DetectFor(string noteId, IEnumerable<RelatedNoteSource> notes, IEnumerable<RelatedPair> excluded = null)
        {
            var context = new Context(options, lexicon, notes);
            var skip = ToSet(excluded);
            var result = new List<RelatedNoteCandidate>();
            if (noteId == null || !context.ById.TryGetValue(noteId, out var target)) return result;
            foreach (var other in context.Profiles)
                if (!ReferenceEquals(other, target)) AddIfRelated(result, context, target, other, skip);
            Sort(result);
            return result;
        }

        /// <summary>既存の候補のうち noteId に関わるものを fresh に入れ替える(DetectFor の結果の反映用)。</summary>
        public static IReadOnlyList<RelatedNoteCandidate> Replace(IEnumerable<RelatedNoteCandidate> existing, string noteId, IEnumerable<RelatedNoteCandidate> fresh)
        {
            var merged = (existing ?? Enumerable.Empty<RelatedNoteCandidate>()).Where(c => !c.Involves(noteId)).ToList();
            merged.AddRange(fresh ?? Enumerable.Empty<RelatedNoteCandidate>());
            Sort(merged);
            return merged;
        }

        private static HashSet<RelatedPair> ToSet(IEnumerable<RelatedPair> excluded)
            => excluded == null ? new HashSet<RelatedPair>() : new HashSet<RelatedPair>(excluded);

        private static void Sort(List<RelatedNoteCandidate> list)
            => list.Sort((x, y) =>
            {
                int byScore = y.Score.CompareTo(x.Score);
                if (byScore != 0) return byScore;
                int byFrom = string.CompareOrdinal(x.From, y.From);
                return byFrom != 0 ? byFrom : string.CompareOrdinal(x.To, y.To);
            });

        private void AddIfRelated(List<RelatedNoteCandidate> result, Context context, Profile x, Profile y, HashSet<RelatedPair> skip)
        {
            var pair = new RelatedPair(x.Id, y.Id);
            if (skip.Contains(pair)) return;
            var reasons = new List<RelatedReason>();
            double sharedProperNounScore = 0;
            var sharedProperNouns = new List<Profile>();

            // 言及(向きごとに別の根拠)
            if (context.Mentions(x).Contains(y.Id))
                reasons.Add(new RelatedReason(RelatedReasonKind.Mention, options.MentionWeight, "「" + x.DisplayName + "」の本文に『" + y.DisplayName + "』が登場"));
            if (context.Mentions(y).Contains(x.Id))
                reasons.Add(new RelatedReason(RelatedReasonKind.Mention, options.MentionWeight, "「" + y.DisplayName + "」の本文に『" + x.DisplayName + "』が登場"));

            // 固有名詞(両方が同じ固有名詞ノートに触れている)
            var xMentions = context.Mentions(x);
            var yMentions = context.Mentions(y);
            foreach (var p in context.ProperNounNotes)
            {
                if (ReferenceEquals(p, x) || ReferenceEquals(p, y)) continue;
                if (xMentions.Contains(p.Id) && yMentions.Contains(p.Id)) sharedProperNouns.Add(p);
            }
            if (sharedProperNouns.Count > 0)
            {
                sharedProperNounScore = options.ProperNounWeight * sharedProperNouns.Count;
                reasons.Add(new RelatedReason(RelatedReasonKind.ProperNoun, sharedProperNounScore,
                    "固有名詞" + Quote(sharedProperNouns.Select(p => p.DisplayName)) + "が共通"));
            }

            // キーワード(固有名詞として数えた語は二重に数えない)
            var shared = x.Keywords.Where(k => y.Keywords.Contains(k) && context.IsUsableKeyword(k)).ToList();
            if (sharedProperNouns.Count > 0)
            {
                var names = new HashSet<string>(sharedProperNouns.SelectMany(p => p.Names));
                shared.RemoveAll(names.Contains);
            }
            if (shared.Count > 0)
            {
                shared.Sort((a, b) => a.Length != b.Length ? b.Length.CompareTo(a.Length) : string.CompareOrdinal(a, b));
                double score = Math.Min(options.MaxKeywordScore, options.KeywordWeight * shared.Count);
                reasons.Add(new RelatedReason(RelatedReasonKind.Keyword, score, "共通するキーワード" + Quote(shared)));
            }

            // 感情(補助)
            var emotion = EmotionReason(x, y);
            if (emotion != null) reasons.Add(emotion);

            double total = reasons.Sum(r => r.Score);
            if (reasons.Count == 0 || total < options.Threshold) return;
            reasons.Sort((a, b) => b.Score.CompareTo(a.Score));
            result.Add(new RelatedNoteCandidate(pair.A, pair.B, total, reasons));
        }

        private RelatedReason EmotionReason(Profile x, Profile y)
        {
            if (lexicon == null || options.EmotionWeight <= 0) return null;
            var a = x.EmotionCounts(lexicon);
            var b = y.EmotionCounts(lexicon);
            double totalA = a.Sum(), totalB = b.Sum();
            if (totalA < options.MinEmotionWords || totalB < options.MinEmotionWords) return null;
            double dot = 0, na = 0, nb = 0, best = -1;
            int bestIndex = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i];
                double shareBoth = Math.Min(a[i] / totalA, b[i] / totalB);
                if (shareBoth > best) { best = shareBoth; bestIndex = i; }
            }
            double similarity = dot / Math.Sqrt(na * nb);
            if (similarity < options.MinEmotionSimilarity) return null;
            return new RelatedReason(RelatedReasonKind.Emotion, options.EmotionWeight * similarity,
                "感情の傾向が近い(" + EmotionNames[bestIndex] + "が多い)");
        }

        private static readonly string[] EmotionNames = { "喜", "怒", "哀", "楽" };

        private static string Quote(IEnumerable<string> terms)
        {
            var list = terms.ToList();
            var sb = new StringBuilder();
            foreach (var t in list.Take(3)) sb.Append('『').Append(t).Append('』');
            if (list.Count > 3) sb.Append("ほか").Append(list.Count - 3).Append("件");
            return sb.ToString();
        }

        /// <summary>1 回の検出で共有する、全ノートの下ごしらえ。</summary>
        private sealed class Context
        {
            private readonly RelatedNoteOptions options;
            private readonly Dictionary<string, int> documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

            public List<Profile> Profiles { get; } = new List<Profile>();
            public Dictionary<string, Profile> ById { get; } = new Dictionary<string, Profile>(StringComparer.Ordinal);
            public List<Profile> ProperNounNotes { get; } = new List<Profile>();

            public Context(RelatedNoteOptions options, EmotionLexicon lexicon, IEnumerable<RelatedNoteSource> notes)
            {
                this.options = options;
                foreach (var note in notes ?? Enumerable.Empty<RelatedNoteSource>())
                {
                    if (note == null || ById.ContainsKey(note.Id)) continue;
                    var profile = new Profile(note, options);
                    ById[note.Id] = profile;
                    Profiles.Add(profile);
                    if (profile.IsProperNoun) ProperNounNotes.Add(profile);
                    foreach (var k in profile.Keywords)
                    {
                        documentFrequency.TryGetValue(k, out int n);
                        documentFrequency[k] = n + 1;
                    }
                }
                Profiles.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            }

            public bool IsUsableKeyword(string keyword)
            {
                if (Profiles.Count < options.CommonKeywordMinNotes) return true;
                return documentFrequency[keyword] / (double)Profiles.Count <= options.MaxKeywordDocumentFraction;
            }

            /// <summary>このノートの本文に名前が出てくる、ほかのノートの ID。</summary>
            public HashSet<string> Mentions(Profile profile)
            {
                if (profile.MentionCache != null) return profile.MentionCache;
                var mentioned = new HashSet<string>(StringComparer.Ordinal);
                foreach (var other in Profiles)
                {
                    if (ReferenceEquals(other, profile)) continue;
                    foreach (var name in other.Names)
                    {
                        if (name.Length < options.MinMentionLength) continue;
                        if (profile.Text.IndexOf(name, StringComparison.Ordinal) >= 0) { mentioned.Add(other.Id); break; }
                    }
                }
                return profile.MentionCache = mentioned;
            }
        }

        /// <summary>1 ノートの下ごしらえ。文字列は照合用にそろえてある(全角英数を半角に、英字を小文字に)。</summary>
        private sealed class Profile
        {
            private double[] emotionCache;

            public string Id { get; }
            public string DisplayName { get; }
            /// <summary>コードと別名行を除いた本文(照合用にそろえたもの)。</summary>
            public string Text { get; }
            /// <summary>照合用にそろえた名前(題名・ファイル名・別名)。</summary>
            public HashSet<string> Names { get; } = new HashSet<string>(StringComparer.Ordinal);
            public bool IsProperNoun { get; }
            public HashSet<string> Keywords { get; }
            public HashSet<string> MentionCache { get; set; }
            private readonly string rawText;

            public Profile(RelatedNoteSource note, RelatedNoteOptions options)
            {
                Id = note.Id;
                string fileName = note.Id.Substring(note.Id.LastIndexOf('/') + 1);
                DisplayName = string.IsNullOrWhiteSpace(note.Title) ? fileName : note.Title.Trim();

                int newline = note.Body.IndexOf('\n');
                string firstLine = newline < 0 ? note.Body : note.Body.Substring(0, newline).TrimEnd('\r');
                var aliases = ProperNounScanner.ParseAliases(firstLine);
                IsProperNoun = aliases.Count > 0;

                AddName(DisplayName);
                AddName(fileName);
                foreach (var alias in aliases) AddName(alias);

                rawText = StripCode(IsProperNoun && newline >= 0 ? note.Body.Substring(newline + 1) : (IsProperNoun ? "" : note.Body));
                Text = ProperNounScanner.Normalize(rawText);
                Keywords = ExtractKeywords(Text);
            }

            private void AddName(string name)
            {
                string normalized = ProperNounScanner.Normalize(name.Trim());
                if (normalized.Length > 0) Names.Add(normalized);
            }

            /// <summary>喜・怒・哀・楽の順の感情語の数。否定された語は数えない。</summary>
            public double[] EmotionCounts(EmotionLexicon lexicon)
            {
                if (emotionCache != null) return emotionCache;
                var counts = new double[4];
                string text = rawText;
                for (int i = 0; i < text.Length;)
                {
                    if (!lexicon.TryMatchAt(text, i, text.Length, out string word, out Emotion? emotion)) { i++; continue; }
                    int after = i + word.Length;
                    if (emotion.HasValue && !lexicon.IsNegated(text, after, Math.Min(text.Length, after + 8)))
                        counts[(int)emotion.Value]++;
                    i = after;
                }
                return emotionCache = counts;
            }
        }

        /// <summary>フェンスコードブロックとインラインコードを空白にする(行数は変えない)。</summary>
        private static string StripCode(string body)
        {
            var sb = new StringBuilder();
            bool fenced = false;
            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    fenced = !fenced;
                    sb.Append('\n');
                    continue;
                }
                if (!fenced)
                {
                    bool inline = false;
                    foreach (char c in line)
                    {
                        if (c == '`') { inline = !inline; sb.Append(' '); }
                        else sb.Append(inline ? ' ' : c);
                    }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private enum Script { Other, Kanji, Katakana, Latin }

        private static Script ScriptOf(char c)
        {
            if ((c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || (c >= '豈' && c <= '﫿') || c == '々') return Script.Kanji;
            if ((c >= 'ァ' && c <= 'ヺ') || c == 'ー') return Script.Katakana;
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_') return Script.Latin;
            return Script.Other;
        }

        /// <summary>同じ文字種の連なりを語とする。形態素解析はしない(漢字 2・カタカナ 2・英数 3 文字以上)。</summary>
        private static HashSet<string> ExtractKeywords(string normalized)
        {
            var keywords = new HashSet<string>(StringComparer.Ordinal);
            int start = 0;
            while (start < normalized.Length)
            {
                var script = ScriptOf(normalized[start]);
                int end = start + 1;
                while (end < normalized.Length && ScriptOf(normalized[end]) == script) end++;
                int length = end - start;
                if (script != Script.Other && length >= (script == Script.Latin ? 3 : 2))
                {
                    string run = normalized.Substring(start, length);
                    if (script != Script.Latin || run.Any(ch => ch < '0' || ch > '9')) keywords.Add(run);
                }
                start = end;
            }
            return keywords;
        }
    }
}
