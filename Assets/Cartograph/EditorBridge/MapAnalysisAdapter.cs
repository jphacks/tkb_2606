using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Analysis;
using Cartograph.Core;
using Cartowords.MapPresentation;

namespace Cartograph.EditorUI
{
    public static class MapAnalysisAdapter
    {
        /// <summary>currentId のノートに関連しそうな相手(自動検出の候補)を、点の高い順に返す。</summary>
        public static IReadOnlyList<string> SuggestedNotes(MapSnapshot snapshot, string currentId)
        {
            if (snapshot == null || currentId == null) return Array.Empty<string>();
            return snapshot.Candidates.Where(c => c.FromNote == currentId || c.ToNote == currentId)
                .OrderByDescending(c => c.Score).ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => c.FromNote == currentId ? c.ToNote : c.FromNote).ToArray();
        }
        public static IReadOnlyList<string> ConnectedNotes(MapSnapshot snapshot, string currentId)
        {
            var connected = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot == null || currentId == null) return Array.Empty<string>();
            var files = snapshot.Nodes.ToDictionary(node => node.Id, node => node.File);
            foreach (var road in snapshot.Roads)
            {
                if (road.Auto) continue;
                string from = files[road.From], to = files[road.To];
                if (from == currentId && to != currentId) connected.Add(to);
                if (to == currentId && from != currentId) connected.Add(from);
            }
            return connected.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }
        public const int MaxCandidatesPerNote = 3;
        // 検出の既定(2.0)は「関連しうる」程度で、地図に出すには多い。言及1件、または根拠の重なりが要るくらいにする。
        public static RelatedNoteOptions DisplayOptions => new RelatedNoteOptions { Threshold = 3.0 };
        /// <summary>地図でつながっていないノートの組を、自動検出した関連ノートの候補にする。</summary>
        public static IReadOnlyList<MapCandidate> Candidates(AnalysisResult result, IEnumerable<RelatedNoteSource> sources,
            IEnumerable<RelatedPair> rejected = null, RelatedNoteDetector detector = null)
        {
            var fileOf = result.Nodes.ToDictionary(n => n.Id, n => n.File.Value, StringComparer.Ordinal);
            var excluded = new HashSet<RelatedPair>(rejected ?? Array.Empty<RelatedPair>());
            foreach (var edge in result.Edges)
                if (!edge.Auto && fileOf[edge.From] != fileOf[edge.To]) excluded.Add(new RelatedPair(fileOf[edge.From], fileOf[edge.To]));
            // 地図の上では、ノートは見出しの最初の拠点で代表させる。
            var anchor = new Dictionary<string, Cartograph.Analysis.Node>(StringComparer.Ordinal);
            foreach (var node in result.Nodes)
                if (!anchor.TryGetValue(node.File.Value, out var kept) || node.Line < kept.Line) anchor[node.File.Value] = node;
            var found = (detector ?? new RelatedNoteDetector(DisplayOptions, EmotionLexicon.Default)).Detect(sources, excluded);
            // 地図が点線だらけにならないよう、1 つのノートにつき点の高い 3 本までにする(Detect は点の高い順)。
            var perNote = new Dictionary<string, int>(StringComparer.Ordinal);
            var limited = new List<RelatedNoteCandidate>();
            foreach (var c in found)
            {
                if (!anchor.ContainsKey(c.From) || !anchor.ContainsKey(c.To)) continue;
                perNote.TryGetValue(c.From, out int fromCount); perNote.TryGetValue(c.To, out int toCount);
                if (fromCount >= MaxCandidatesPerNote || toCount >= MaxCandidatesPerNote) continue;
                perNote[c.From] = fromCount + 1; perNote[c.To] = toCount + 1; limited.Add(c);
            }
            return limited
                .Select(c => new MapCandidate(anchor[c.From].Id, anchor[c.To].Id, c.From, c.To, c.Score,
                    string.Join(" / ", c.Reasons.Take(3).Select(r => r.Label)))).ToArray();
        }
        public static async Task<MapSnapshot> Analyze(ProjectEditorWorkspace workspace, MapRequest request, Func<bool> current,
            IReadOnlyCollection<RelatedPair> rejected = null)
        {
            var tree = workspace.Tree;
            void Check()
            {
                if (!current() || !ReferenceEquals(tree, workspace.Tree) ||
                    !Matches(request.NoteRevisions, workspace.Documents.ToDictionary(d => d.id, d => d.revision)))
                    throw new OperationCanceledException();
            }
            Check();
            foreach (var id in request.NoteRevisions.Keys) { Check(); await workspace.LoadDocument(id); Check(); }
            // No await between copies and version checks: capture on the Unity UI context.
            var inputs = request.NoteRevisions.Select(pair => new AnalysisInput(NotePath.Parse(pair.Key),
                workspace.GetLabeledDocument(pair.Key), pair.Value)).ToArray();
            var sources = workspace.Documents.Where(d => request.NoteRevisions.ContainsKey(d.id))
                .Select(d => new RelatedNoteSource(d.id, d.title, d.markdown)).ToArray();
            Check();
            // 候補の検出は、解析と同じ背景スレッドで行う。失敗しても地図そのものは出す。
            var analyzed = await Task.Run(() =>
            {
                var analysis = ProjectAnalyzer.Analyze(inputs, EmotionLexicon.Default);
                IReadOnlyList<MapCandidate> candidates;
                try { candidates = Candidates(analysis, sources, rejected); }
                catch (Exception) { candidates = Array.Empty<MapCandidate>(); }
                return new KeyValuePair<AnalysisResult, IReadOnlyList<MapCandidate>>(analysis, candidates);
            });
            Check();
            return Convert(request, analyzed.Key, analyzed.Value);
        }
        public static bool Matches(IReadOnlyDictionary<string, long> expected, IReadOnlyDictionary<string, long> actual) =>
            expected.Count == actual.Count && expected.All(p => actual.TryGetValue(p.Key, out var revision) && p.Value == revision);
        public static MapSnapshot Convert(MapRequest request, AnalysisResult result, IEnumerable<MapCandidate> candidates = null)
        {
            if (!Matches(request.NoteRevisions, result.Revisions.ToDictionary(p => p.Key.Value, p => p.Value)))
                throw new InvalidOperationException("解析結果の版が入力と一致しません。");
            return new MapSnapshot(request,
                result.Nodes.Select(n => new MapNode(n.Id, n.File.Value, n.Title, n.Line, n.TypedChars, n.Isolated, n.Level)),
                result.Edges.Select(e => new MapRoad(e.From, e.To, Road(e.Kind), e.Auto)),
                result.Fogs.Select(f => new MapFog(f.Node, f.Line)),
                result.Marks.Select(m => new MapMark(m.Node, Mark(m.Type), m.Message)),
                result.Flowers.Select(f => new MapFlower(f.Node, f.Line, Flower(f.Emotion), f.Withered)),
                buildings: result.Buildings.Select(b => new MapBuilding(b.Node, b.ProperNounNote.Value, b.ProperNounNote.Name, b.Strong, b.Medium, b.Weak)),
                candidates: candidates);
        }
        private static RoadKind Road(EdgeKind value)
        {
            switch (value) { case EdgeKind.Unspecified: return RoadKind.Unspecified; case EdgeKind.Premise: return RoadKind.Premise;
                case EdgeKind.Contrast: return RoadKind.Contrast; case EdgeKind.Example: return RoadKind.Example;
                default: throw new ArgumentOutOfRangeException(nameof(value)); }
        }
        private static MarkKind Mark(MarkType value)
        {
            switch (value) { case MarkType.NoReason: return MarkKind.MissingReason; case MarkType.BrokenLink: return MarkKind.BrokenLink;
                case MarkType.Isolated: return MarkKind.Isolated; default: throw new ArgumentOutOfRangeException(nameof(value)); }
        }
        private static Cartowords.MapPresentation.Emotion Flower(Cartograph.Analysis.Emotion value)
        {
            switch (value) { case Cartograph.Analysis.Emotion.Joy: return Cartowords.MapPresentation.Emotion.Joy;
                case Cartograph.Analysis.Emotion.Anger: return Cartowords.MapPresentation.Emotion.Anger;
                case Cartograph.Analysis.Emotion.Sorrow: return Cartowords.MapPresentation.Emotion.Sadness;
                case Cartograph.Analysis.Emotion.Pleasure: return Cartowords.MapPresentation.Emotion.Delight;
                default: throw new ArgumentOutOfRangeException(nameof(value)); }
        }
    }
}
