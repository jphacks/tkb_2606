using System;
using System.Collections.Generic;
using Cartograph.Core;

namespace Cartograph.Analysis
{
    /// <summary>
    /// 全ノートの本文を読み、地図全体の解析結果を作る。リンクの宛先の解決、孤島、リンク切れを扱う。
    /// 状態を持たず、呼ぶたびにすべてのノートを解析し直す(revision は版の照合のために結果へ引き継ぐだけで、再利用の鍵には使わない)。
    ///
    /// リンクの解決:
    ///   - "[[ノート名]]" は、同名のノートが 1 つだけならそのノートの最初の拠点につなぐ。複数あれば「行き先が複数」のリンク切れ。
    ///   - "[[フォルダ/…/ノート名]]" は、notes/ からの相対パスが一致するノートにつなぐ。
    ///   - "[[ノート名#見出し]]"、"[[#見出し]]"(同じファイル)は、題名が一致する拠点につなぐ。同じ題名が複数あれば先頭のもの。
    ///   - 宛先がなければ「宛先なし」のリンク切れ。リンク切れでは Edge を作らない。
    ///   - 自分自身の拠点へのリンクは、道を作らない。
    /// 孤島: 見出しの親子の道とノート内のリンクを除き、ほかのノートと 1 本もつながっていないノート。そのノートの最初の拠点に付ける。
    /// </summary>
    public static class ProjectAnalyzer
    {
        public static AnalysisResult Analyze(IEnumerable<AnalysisInput> inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            var notes = new List<NoteAnalysis>();
            var seen = new HashSet<NotePath>();
            foreach (var input in inputs)
            {
                if (input == null) throw new ArgumentException("入力に null が含まれています。", nameof(inputs));
                if (!seen.Add(input.NoteId))
                    throw new ArgumentException("同じノートが 2 回渡されました: " + input.NoteId, nameof(inputs));
                notes.Add(NoteParser.Parse(input));
            }
            return Resolve(notes);
        }

        /// <summary>解析済みのノート単位の結果から、地図全体の結果を作る。</summary>
        public static AnalysisResult Resolve(IReadOnlyList<NoteAnalysis> notes)
        {
            if (notes == null) throw new ArgumentNullException(nameof(notes));

            var byPath = new Dictionary<NotePath, int>();
            var byName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < notes.Count; i++)
            {
                var id = notes[i].NoteId;
                if (!byPath.ContainsKey(id)) byPath.Add(id, i);
                if (!byName.TryGetValue(id.Name, out var list)) byName[id.Name] = list = new List<int>();
                list.Add(i);
            }

            var edges = new List<Edge>();
            var marks = new List<Mark>();
            var connected = new bool[notes.Count];     // ほかのノートと道でつながっているか

            // ノートごとの順に並べるため、先にノート単位の結果を集める。
            var edgesByNote = new List<Edge>[notes.Count];
            var marksByNote = new List<Mark>[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                var noteEdges = new List<Edge>(note.AutoEdges);
                var noteMarks = new List<Mark>(note.Marks);

                foreach (var link in note.Links)
                {
                    var res = ResolveTarget(link, i, notes, byPath, byName, out Node target, out int targetNote);
                    if (res != MarkDetail.None)
                    {
                        noteMarks.Add(new Mark(MarkType.BrokenLink, res, link.FromNode, link.Line, link.Target));
                        continue;
                    }
                    if (target.Id == link.FromNode) continue;

                    bool cross = note.NoteId.FolderPath != notes[targetNote].NoteId.FolderPath;
                    noteEdges.Add(new Edge(link.FromNode, target.Id, link.Kind, link.Reason, cross, false));

                    if (targetNote != i)
                    {
                        connected[i] = true;
                        connected[targetNote] = true;
                    }
                }
                edgesByNote[i] = noteEdges;
                marksByNote[i] = noteMarks;
            }

            var nodes = new List<Node>();
            var fogs = new List<Fog>();
            var flowers = new List<Flower>();
            var revisions = new Dictionary<NotePath, long>();
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                revisions[note.NoteId] = note.Revision;
                bool isolated = !connected[i];
                for (int n = 0; n < note.Nodes.Count; n++)
                {
                    var node = note.Nodes[n];
                    if (n == 0 && isolated)
                    {
                        node = node.WithIsolated(true);
                        marksByNote[i].Add(new Mark(MarkType.Isolated, MarkDetail.None, node.Id, node.Line, ""));
                    }
                    nodes.Add(node);
                }
                edges.AddRange(edgesByNote[i]);
                marks.AddRange(marksByNote[i]);
                fogs.AddRange(note.Fogs);
                flowers.AddRange(note.Flowers);
            }

            return new AnalysisResult(nodes, edges, fogs, marks, flowers, revisions);
        }

        // 宛先を探す。見つかれば None を返して target / targetNote を設定する。
        private static MarkDetail ResolveTarget(NoteLink link, int sourceIndex, IReadOnlyList<NoteAnalysis> notes,
            Dictionary<NotePath, int> byPath, Dictionary<string, List<int>> byName, out Node target, out int targetNote)
        {
            target = null;
            targetNote = -1;

            if (link.TargetNote.Length == 0)
            {
                targetNote = sourceIndex;
            }
            else if (link.TargetNote.IndexOf('/') >= 0)
            {
                if (!NotePath.TryParse(link.TargetNote, out var path) || !byPath.TryGetValue(path, out targetNote))
                    return MarkDetail.Missing;
            }
            else
            {
                if (!byName.TryGetValue(link.TargetNote, out var candidates)) return MarkDetail.Missing;
                if (candidates.Count > 1) return MarkDetail.Ambiguous;
                targetNote = candidates[0];
            }

            var nodes = notes[targetNote].Nodes;
            if (nodes.Count == 0) return MarkDetail.Missing;
            if (link.TargetHeading == null)
            {
                target = nodes[0];
                return MarkDetail.None;
            }
            foreach (var node in nodes)
            {
                if (string.Equals(node.Title, link.TargetHeading, StringComparison.Ordinal))
                {
                    target = node;
                    return MarkDetail.None;
                }
            }
            return MarkDetail.Missing;
        }
    }
}
