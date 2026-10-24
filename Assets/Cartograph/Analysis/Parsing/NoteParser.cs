using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cartograph.Core;

namespace Cartograph.Analysis
{
    /// <summary>
    /// 1 つのノートを、行を上から順に読む単純な走査と正規表現で解析する。
    /// コードブロック(``` で囲まれた範囲)の中は解析しない。リンクの宛先の解決と孤島の判定は、地図全体を見る側で行う。
    ///
    /// 見出しより前の本文(リンク・霧・本文量)は、ファイルの最初の見出しの所属にする。
    /// 種別のある行(==&gt; など)は、最初のリンクだけが種別と理由を持つ。同じ行のほかのリンクは、種別のないふつうのリンクとして扱う。
    /// </summary>
    public static class NoteParser
    {
        private static readonly Regex HeadingRegex = new Regex(@"^(#{1,3})[ \t]+(.*?)[ \t]*$", RegexOptions.Compiled);
        private static readonly Regex ClosingHashesRegex = new Regex(@"[ \t]+#+$", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[\[([^\[\]\r\n]+)\]\]", RegexOptions.Compiled);
        private static readonly Regex KindRegex = new Regex(@"^[ \t]*(==>|<->|\.\.>)", RegexOptions.Compiled);
        private static readonly Regex UrlRegex = new Regex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^\s<>()\[\]「」]*", RegexOptions.Compiled);

        private struct Line
        {
            public int Number;     // 1 始まり
            public int Start;
            public int Length;
            public bool InCode;    // フェンス行を含む、コードブロックの範囲
            public int Level;      // 見出しなら 1〜3、そうでなければ 0
            public string Title;
        }

        public static NoteAnalysis Parse(AnalysisInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            string text = input.Text;
            var lines = SplitLines(text);
            int[] typedPrefix = BuildTypedPrefix(input.Document);

            var nodes = new List<Node>();
            var autoEdges = new List<Edge>();
            var ownerOfLine = new int[lines.Count];     // 行の所属拠点の index
            var headingLines = new List<int>();
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].Level > 0) headingLines.Add(i);

            var typedByNode = new List<int>();
            if (headingLines.Count == 0)
            {
                nodes.Add(null);
                typedByNode.Add(0);
                for (int i = 0; i < lines.Count; i++) ownerOfLine[i] = 0;
            }
            else
            {
                int current = 0;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var stack = new List<(int Level, string Id, string Path)>();
                int firstHeadingLine = headingLines[0];
                var headingIndexByLine = new Dictionary<int, int>();
                string firstId = null;
                for (int h = 0; h < headingLines.Count; h++)
                {
                    var line = lines[headingLines[h]];
                    while (stack.Count > 0 && stack[stack.Count - 1].Level >= line.Level)
                        stack.RemoveAt(stack.Count - 1);

                    string parentPath = stack.Count > 0 ? stack[stack.Count - 1].Path : null;
                    string segment = line.Title;
                    string path = parentPath == null ? segment : parentPath + "/" + segment;
                    string id = input.NoteId.Value + "#" + path;
                    for (int n = 2; !ids.Add(id); n++)
                    {
                        segment = line.Title + "~" + n;
                        path = parentPath == null ? segment : parentPath + "/" + segment;
                        id = input.NoteId.Value + "#" + path;
                    }

                    if (h == 0) firstId = id;
                    if (stack.Count > 0)
                        autoEdges.Add(new Edge(stack[stack.Count - 1].Id, id, EdgeKind.Unspecified, null, false, true));
                    else if (h > 0)
                        autoEdges.Add(new Edge(firstId, id, EdgeKind.Unspecified, null, false, true));

                    stack.Add((line.Level, id, path));
                    headingIndexByLine[headingLines[h]] = nodes.Count;
                    nodes.Add(new Node(id, input.NoteId, line.Title, line.Level, line.Number, line.Start, 0, false));
                    typedByNode.Add(0);
                }

                for (int i = 0; i < lines.Count; i++)
                {
                    if (headingIndexByLine.TryGetValue(i, out int idx)) current = idx;
                    else if (i < firstHeadingLine) current = 0;
                    ownerOfLine[i] = current;
                }
            }

            // 本文量: 見出し行・コードブロックを除く、typed かつ空白でない文字。
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.InCode || line.Level > 0) continue;
                typedByNode[ownerOfLine[i]] += typedPrefix[line.Start + line.Length] - typedPrefix[line.Start];
            }

            if (headingLines.Count == 0)
            {
                nodes[0] = new Node(input.NoteId.Value + "#", input.NoteId, input.NoteId.Name, 0, 1, 0, typedByNode[0], false);
            }
            else
            {
                for (int n = 0; n < nodes.Count; n++)
                {
                    var o = nodes[n];
                    nodes[n] = new Node(o.Id, o.File, o.Title, o.Level, o.Line, o.Position, typedByNode[n], false);
                }
            }

            var links = new List<NoteLink>();
            var fogs = new List<Fog>();
            var marks = new List<Mark>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.InCode) continue;
                string body = text.Substring(line.Start, line.Length);
                string nodeId = nodes[ownerOfLine[i]].Id;

                if (IsFog(body)) fogs.Add(new Fog(nodeId, line.Number, body));
                if (line.Level > 0) continue;
                ParseLinks(body, line.Number, nodeId, links, marks);
            }

            return new NoteAnalysis(input.NoteId, input.Revision, nodes, autoEdges, links, fogs, null, marks);
        }

        private static List<Line> SplitLines(string text)
        {
            var lines = new List<Line>();
            bool inFence = false;
            int pos = 0, number = 1;
            while (pos <= text.Length)
            {
                if (pos == text.Length && pos > 0 && IsTerminator(text[pos - 1])) break;
                int end = pos;
                while (end < text.Length && !IsTerminator(text[end])) end++;
                int length = end - pos;
                string content = text.Substring(pos, length);

                var line = new Line { Number = number, Start = pos, Length = length };
                bool isFence = content.TrimStart(' ', '\t').StartsWith("```", StringComparison.Ordinal);
                if (isFence)
                {
                    line.InCode = true;
                    inFence = !inFence;
                }
                else if (inFence)
                {
                    line.InCode = true;
                }
                else
                {
                    var m = HeadingRegex.Match(content);
                    if (m.Success)
                    {
                        string title = ClosingHashesRegex.Replace(m.Groups[2].Value, "").Trim();
                        if (title.Length > 0)
                        {
                            line.Level = m.Groups[1].Length;
                            line.Title = title;
                        }
                    }
                }
                lines.Add(line);

                number++;
                if (end >= text.Length) break;
                pos = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            }
            return lines;
        }

        private static bool IsTerminator(char c) => c == '\n' || c == '\r';

        // prefix[i] = text[0..i) のうち、typed かつ空白でない文字の数。
        private static int[] BuildTypedPrefix(LabeledText doc)
        {
            string text = doc.Text;
            var prefix = new int[text.Length + 1];
            int pos = 0;
            int count = 0;
            foreach (var run in doc.Labels.Runs)
            {
                for (int k = 0; k < run.Length; k++, pos++)
                {
                    if (run.Origin == Origin.Typed && !char.IsWhiteSpace(text[pos])) count++;
                    prefix[pos + 1] = count;
                }
            }
            return prefix;
        }

        private static bool IsFog(string line)
        {
            if (line.IndexOf("TODO:", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("TODO：", StringComparison.Ordinal) >= 0) return true;
            string withoutUrls = UrlRegex.Replace(line, "");
            return withoutUrls.IndexOf('?') >= 0 || withoutUrls.IndexOf('？') >= 0;
        }

        private static void ParseLinks(string body, int lineNumber, string nodeId, List<NoteLink> links, List<Mark> marks)
        {
            var kindMatch = KindRegex.Match(body);
            EdgeKind kind = EdgeKind.Unspecified;
            if (kindMatch.Success)
            {
                switch (kindMatch.Groups[1].Value)
                {
                    case "==>": kind = EdgeKind.Premise; break;
                    case "<->": kind = EdgeKind.Contrast; break;
                    default: kind = EdgeKind.Example; break;
                }
            }

            bool first = true;
            foreach (Match m in LinkRegex.Matches(body))
            {
                string target = m.Groups[1].Value.Trim();
                SplitTarget(target, out string note, out string heading);
                if (note.Length == 0 && heading == null) { first = false; continue; }

                if (first && kind != EdgeKind.Unspecified)
                {
                    string reason = ReadReason(body, m.Index + m.Length);
                    links.Add(new NoteLink(nodeId, lineNumber, target, note, heading, kind, reason));
                    if (reason == null)
                        marks.Add(new Mark(MarkType.NoReason, MarkDetail.None, nodeId, lineNumber, target));
                }
                else
                {
                    links.Add(new NoteLink(nodeId, lineNumber, target, note, heading, EdgeKind.Unspecified, null));
                }
                first = false;
            }
        }

        private static void SplitTarget(string target, out string note, out string heading)
        {
            int hash = target.IndexOf('#');
            if (hash < 0) { note = target; heading = null; return; }
            note = target.Substring(0, hash).Trim();
            string h = target.Substring(hash + 1).Trim();
            heading = h.Length == 0 ? null : h;
        }

        // リンクの後ろの ':'(全角も可)に続く理由。なければ null。
        private static string ReadReason(string body, int afterLink)
        {
            string rest = body.Substring(afterLink).TrimStart(' ', '\t');
            if (rest.Length == 0 || (rest[0] != ':' && rest[0] != '：')) return null;
            string reason = rest.Substring(1).Trim();
            return reason.Length == 0 ? null : reason;
        }
    }
}
